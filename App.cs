using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexUsageWatch {
public sealed class Settings {
    public string Token="";
    public string ChatId="";
    public string BotName="";
    public string CodexPath="";
}
public static class Storage {
    public static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageWatch");
    static readonly byte[] Entropy=Encoding.UTF8.GetBytes("CodexUsageWatch.v1");
    public static void Write(string name,object data,bool secret) {
        Directory.CreateDirectory(Root);
        byte[] b=Encoding.UTF8.GetBytes(Json.Dump(data));
        if(secret) b=ProtectedData.Protect(b,Entropy,DataProtectionScope.CurrentUser);
        string path=Path.Combine(Root,name), temp=path+".tmp";
        File.WriteAllBytes(temp,b);
        if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    public static T Read<T>(string name,bool secret) where T:new() {
        string path=Path.Combine(Root,name);
        if(!File.Exists(path)) return new T();
        byte[] b=File.ReadAllBytes(path);
        if(secret) b=ProtectedData.Unprotect(b,Entropy,DataProtectionScope.CurrentUser);
        return Json.Serializer.Deserialize<T>(Encoding.UTF8.GetString(b));
    }
}
public sealed class CodexClient : IDisposable {
    Process process;
    int nextId;
    string path;
    public CodexClient(string custom) { path=FindCodex(custom); }
    public static string FindCodex(string custom) {
        if(!String.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return custom;
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(root)) {
            string p=Directory.GetDirectories(root).Select(d=>Path.Combine(d,"codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(p!=null) return p;
        }
        foreach(string d in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) {
            try { string p=Path.Combine(d.Trim('"'),"codex.exe"); if(File.Exists(p)) return p; } catch { }
        }
        throw new InvalidOperationException("Codex executable was not found. Use Choose Codex executable in the tray menu.");
    }
    async Task<object> Request(string method,object args) {
        int id=++nextId;
        var req=new Dictionary<string,object>{{"id",id},{"method",method}};
        if(args!=null) req["params"]=args;
        process.StandardInput.WriteLine(Json.Dump(req)); process.StandardInput.Flush();
        DateTime end=DateTime.UtcNow.AddSeconds(20);
        while(true) {
            Task<string> read=process.StandardOutput.ReadLineAsync();
            int ms=(int)Math.Max(1,(end-DateTime.UtcNow).TotalMilliseconds);
            if(await Task.WhenAny(read,Task.Delay(ms))!=read) throw new TimeoutException("Codex usage request timed out.");
            string line=await read;
            if(line==null) throw new IOException("Codex usage connection closed.");
            var msg=Json.Parse(line);
            if(Json.Str(Json.Get(msg,"id"))!=id.ToString(CultureInfo.InvariantCulture)) continue;
            if(Json.Get(msg,"error")!=null) {
                string code=Json.Str(Json.Get(Json.Get(msg,"error"),"code"));
                throw new InvalidOperationException("Codex could not read usage (code "+code+"). Check the active sign-in in Codex; the monitor will retry.");
            }
            return Json.Get(msg,"result");
        }
    }
    async Task Connect() {
        process=new Process();
        process.StartInfo=new ProcessStartInfo(path,"app-server --stdio") {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=Storage.Root,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        process.ErrorDataReceived+=(s,e)=>{};
        process.Start(); process.BeginErrorReadLine();
        await Request("initialize",new { clientInfo=new { name="codex_usage_watch", title="Codex Usage Watch", version="1.0.0" }, capabilities=new { experimentalApi=true } });
        process.StandardInput.WriteLine("{\"method\":\"initialized\"}"); process.StandardInput.Flush();
    }
    public async Task<WeeklyUsage> Read() {
        try {
            if(process==null || process.HasExited) await Connect();
            var u=WeeklyUsage.Parse(await Request("account/rateLimits/read",null));
            if(u.Reset<=DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidOperationException("Codex returned an expired weekly window; waiting for refreshed usage.");
            return u;
        } catch { Dispose(); throw; }
    }
    public void Dispose() {
        if(process==null) return;
        try { if(!process.HasExited) { process.StandardInput.Close(); if(!process.WaitForExit(500)) process.Kill(); } } catch { }
        process.Dispose(); process=null;
    }
}
public sealed class TelegramClient : IDisposable {
    readonly HttpClient http=new HttpClient { Timeout=TimeSpan.FromSeconds(12) };
    public async Task<object> Call(string token,string method,Dictionary<string,string> values) {
        try {
            using(var content=new FormUrlEncodedContent(values))
            using(var response=await http.PostAsync("https://api.telegram.org/bot"+token+"/"+method,content)) {
                if(!response.IsSuccessStatusCode) throw new InvalidOperationException("Telegram returned HTTP "+(int)response.StatusCode+". Check the token, chat permissions, or retry later.");
                var result=Json.Parse(await response.Content.ReadAsStringAsync());
                if(!Object.Equals(Json.Get(result,"ok"),true)) throw new InvalidOperationException("Telegram rejected the request. Check the bot configuration.");
                return Json.Get(result,"result");
            }
        } catch(TaskCanceledException) { throw new InvalidOperationException("Telegram request timed out. Phone delivery is unconfirmed."); }
          catch(HttpRequestException) { throw new InvalidOperationException("Telegram could not be reached. Check the internet connection."); }
    }
    public Task<object> Send(Settings s,string text) {
        return Call(s.Token,"sendMessage",new Dictionary<string,string>{{"chat_id",s.ChatId},{"text",text},{"disable_notification","false"},{"protect_content","true"}});
    }
    public void Dispose() { http.Dispose(); }
}
public sealed class WatchForm : Form {
    readonly NotifyIcon tray=new NotifyIcon();
    readonly TelegramClient telegram=new TelegramClient();
    CodexClient codex;
    Settings settings;
    AlertState state;
    WeeklyUsage last;
    readonly Label allowance=new Label(), usageStatus=new Label(), phoneStatus=new Label(), pairStatus=new Label(), errorStatus=new Label();
    readonly TextBox token=new TextBox();
    readonly Button link=new Button(), finish=new Button(), test=new Button();
    readonly CheckBox startup=new CheckBox();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    bool quitting, reading, sending, pairing;
    int failures, telegramFailures;
    DateTime nextPoll=DateTime.MinValue,lastRead=DateTime.MinValue,nextTelegram=DateTime.MinValue;
    string challenge="", pendingToken="", pendingBot="";
    DateTime challengeExpires;
    static readonly string StartupPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Codex Usage Watch.lnk");
    public WatchForm(bool show) {
        Directory.CreateDirectory(Storage.Root);
        string loadError="";
        try { settings=Storage.Read<Settings>("settings.dat",true); } catch { settings=new Settings(); loadError="Saved Telegram settings could not be decrypted. Link the bot again."; }
        try { state=Storage.Read<AlertState>("alert-state.json",false); } catch { state=new AlertState(); loadError="Alert history could not be read. A low-usage alert may repeat."; }
        Text="Codex Usage Watch"; Size=new Size(750,680); MinimumSize=new Size(700,650);
        Font=new Font("Segoe UI",10F); AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
        Icon=SystemIcons.Information;
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=13,Padding=new Padding(22),AutoScroll=true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(panel);
        var heading=new Label { Text="Weekly usage alerts",Font=new Font("Segoe UI",18F,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,10) }; panel.Controls.Add(heading);
        allowance.Text="Connecting to Codex..."; allowance.Font=new Font("Segoe UI",24F,FontStyle.Bold); allowance.AutoSize=true; panel.Controls.Add(allowance);
        usageStatus.Text="Alert at 2% remaining. Checks every 30 seconds; every 5 seconds at 10% or below."; usageStatus.AutoSize=true; usageStatus.MaximumSize=new Size(650,0); usageStatus.Margin=new Padding(0,8,0,12); panel.Controls.Add(usageStatus);
        errorStatus.AutoSize=true; errorStatus.MaximumSize=new Size(650,0); errorStatus.ForeColor=Color.DarkRed; panel.Controls.Add(errorStatus);
        var guide=new Label { AutoSize=true,MaximumSize=new Size(650,0),Margin=new Padding(0,12,0,8),Text="Telegram setup: use a dedicated bot from @BotFather (/newbot), or an unused bot you own. Get its API token with /mybots > select bot > API Token. Paste it below; it is encrypted for your Windows account. No token needs to be sent in chat." }; panel.Controls.Add(guide);
        token.UseSystemPasswordChar=true; token.Dock=DockStyle.Top; token.Text=settings.Token; token.AccessibleName="Telegram bot token"; panel.Controls.Add(token);
        var buttons=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top,Margin=new Padding(0,8,0,8) };
        link.Text="1. Prepare Telegram link"; link.AutoSize=true; link.Click+=async(s,e)=>await PreparePair(); buttons.Controls.Add(link);
        finish.Text="2. Finish linking"; finish.AutoSize=true; finish.Enabled=false; finish.Click+=async(s,e)=>await FinishPair(); buttons.Controls.Add(finish); panel.Controls.Add(buttons);
        pairStatus.AutoSize=true; pairStatus.MaximumSize=new Size(650,0); pairStatus.Text="Prepare the link, then send the displayed /start command to your bot."; panel.Controls.Add(pairStatus);
        phoneStatus.AutoSize=true; phoneStatus.MaximumSize=new Size(650,0); phoneStatus.Margin=new Padding(0,10,0,8); panel.Controls.Add(phoneStatus); UpdatePhoneStatus();
        var actions=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top };
        test.Text="Send test alert"; test.AutoSize=true; test.Click+=async(s,e)=>await TestAlert(); actions.Controls.Add(test);
        var refresh=new Button { Text="Check usage now",AutoSize=true }; refresh.Click+=(s,e)=>{nextPoll=DateTime.MinValue;}; actions.Controls.Add(refresh);
        var hide=new Button { Text="Run in background",AutoSize=true }; hide.Click+=(s,e)=>Hide(); actions.Controls.Add(hide); panel.Controls.Add(actions);
        startup.Text="Start when I sign into Windows"; startup.AutoSize=true; startup.Checked=File.Exists(StartupPath); startup.Margin=new Padding(0,10,0,4);
        startup.CheckedChanged+=(s,e)=>{try { SetStartup(startup.Checked); } catch { MessageBox.Show(this,"Could not update the Startup shortcut. Run install.ps1 to register startup.",Text); }}; panel.Controls.Add(startup);
        panel.Controls.Add(new Label { Text="Closing this window keeps monitoring in the tray. Use Exit in the tray menu to stop.\nPhone alerts need internet, Telegram notifications, and an awake PC. The app never uses a reset or stops a task.",AutoSize=true,MaximumSize=new Size(650,0),ForeColor=Color.DimGray,Margin=new Padding(0,12,0,0) });
        tray.Icon=SystemIcons.Information; tray.Visible=true; tray.Text="Codex Usage Watch: connecting";
        var menu=new ContextMenuStrip();
        menu.Items.Add("Open monitor",null,(s,e)=>ShowMonitor());
        menu.Items.Add("Check usage now",null,(s,e)=>nextPoll=DateTime.MinValue);
        menu.Items.Add("Choose Codex executable",null,(s,e)=>ChooseCodex());
        menu.Items.Add("Exit",null,(s,e)=>{quitting=true;Close();}); tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(s,e)=>ShowMonitor(); tray.BalloonTipClicked+=(s,e)=>ShowMonitor();
        FormClosing+=(s,e)=>{if(!quitting) {e.Cancel=true;Hide();}};
        FormClosed+=(s,e)=>{timer.Stop();tray.Visible=false;tray.Dispose();if(codex!=null)codex.Dispose();telegram.Dispose();};
        errorStatus.Text=loadError;
        timer.Interval=500; timer.Tick+=async(s,e)=>await Tick(); timer.Start();
        Shown+=(s,e)=>{if(!show)Hide();};
    }
    public void ShowMonitor() { Show();WindowState=FormWindowState.Normal;Activate(); }
    void UpdatePhoneStatus() { phoneStatus.Text=String.IsNullOrEmpty(settings.ChatId)?"Telegram: not linked. Windows alerts are active.":"Telegram: linked to @"+settings.BotName+". Use Send test alert to verify your phone."; }
    void SaveSettings() { Storage.Write("settings.dat",settings,true); }
    void SaveState() { Storage.Write("alert-state.json",state,false); }
    void LocalAlert(string text) {
        tray.ShowBalloonTip(15000,"Codex weekly usage alert",text,ToolTipIcon.Warning);
        // Separate sound is intentional so a notification without its default sound is still audible.
        SystemSounds.Exclamation.Play();
    }
    string AlertText() {
        string reset=DateTimeOffset.FromUnixTimeSeconds(last.Reset).LocalDateTime.ToString("MMM d, h:mm tt");
        return "Codex weekly allowance: "+last.Remaining.ToString("0.##",CultureInfo.InvariantCulture)+"% remaining. Wrap up your tasks or use a banked reset. Weekly renewal: "+reset+" (PC local time).";
    }
    async Task Tick() {
        if(quitting) return;
        if(!reading && DateTime.UtcNow>=nextPoll) {
            reading=true;
            try {
                if(codex==null)codex=new CodexClient(settings.CodexPath);
                last=await codex.Read(); if(quitting)return;
                failures=0;lastRead=DateTime.UtcNow;
                bool wasTelegramSent=state.TelegramSent;
                state.Observe(last);
                if(wasTelegramSent && !state.TelegramSent) { telegramFailures=0;nextTelegram=DateTime.MinValue; }
                SaveState();
                allowance.Text=last.Remaining.ToString("0.##",CultureInfo.InvariantCulture)+"% remaining";
                usageStatus.Text="Updated "+DateTime.Now.ToString("h:mm:ss tt")+". Weekly renewal: "+DateTimeOffset.FromUnixTimeSeconds(last.Reset).LocalDateTime.ToString("MMM d, h:mm tt")+".\nAlert at 2% remaining; checking every "+Schedule.PollSeconds(last,0)+" seconds.";
                tray.Text="Codex weekly: "+last.Remaining.ToString("0.##",CultureInfo.InvariantCulture)+"% remaining";
                errorStatus.Text="";
                if(state.NeedsLocal(last)) { LocalAlert(AlertText());state.LocalSent=true;SaveState(); }
            } catch(Exception e) {
                if(quitting)return;
                failures++;
                errorStatus.Text="Usage monitor: "+SafeError(e)+" Retrying in "+Schedule.PollSeconds(last,failures)+" seconds.";
                tray.Text="Codex Usage Watch: usage unavailable";
                allowance.Text=last==null?"Usage unavailable":last.Remaining.ToString("0.##",CultureInfo.InvariantCulture)+"% (last known)";
                if(codex!=null) {codex.Dispose();codex=null;}
            } finally {nextPoll=DateTime.UtcNow.AddSeconds(Schedule.PollSeconds(last,failures));reading=false;}
        }
        if(!sending && last!=null && failures==0 && (DateTime.UtcNow-lastRead).TotalSeconds<45 && state.NeedsTelegram(last) && !String.IsNullOrEmpty(settings.ChatId) && DateTime.UtcNow>=nextTelegram) await DeliverPhone();
    }
    async Task DeliverPhone() {
        sending=true;
        string window=last.Key; double used=last.Used; string chat=settings.ChatId;
        try {
            await telegram.Send(settings,AlertText()); if(quitting)return;
            if(state.WindowKey==window && state.LastUsed>=used && settings.ChatId==chat) { state.TelegramSent=true;SaveState(); }
            telegramFailures=0;phoneStatus.Text="Telegram: alert accepted at "+DateTime.Now.ToString("h:mm:ss tt")+". Check your phone.";
        } catch(Exception e) {
            if(quitting)return;
            telegramFailures++;int retry=Math.Min(300,30*(1<<Math.Min(telegramFailures-1,4)));
            nextTelegram=DateTime.UtcNow.AddSeconds(retry);
            phoneStatus.Text="Telegram: "+SafeError(e)+" Retrying in "+retry+" seconds. Windows alert is independent.";
        } finally {sending=false;}
    }
    static string SafeError(Exception e) {
        if(e is InvalidOperationException || e is TimeoutException || e is IOException) return e is IOException?"Local usage connection or state storage is unavailable.":e.Message;
        return "An unexpected error occurred ("+e.GetType().Name+").";
    }
    async Task PreparePair() {
        if(pairing)return;pairing=true;link.Enabled=false;finish.Enabled=false;
        try {
            string candidate=token.Text.Trim();
            if(!System.Text.RegularExpressions.Regex.IsMatch(candidate,@"^\d+:[A-Za-z0-9_-]{20,}$"))throw new InvalidOperationException("Paste the full bot token supplied by BotFather.");
            var me=await telegram.Call(candidate,"getMe",new Dictionary<string,string>());
            var hook=await telegram.Call(candidate,"getWebhookInfo",new Dictionary<string,string>());
            if(!String.IsNullOrEmpty(Json.Str(Json.Get(hook,"url"))))throw new InvalidOperationException("This bot already has a webhook. Use a dedicated bot; this tool will not change the existing webhook.");
            pendingToken=candidate;pendingBot=Json.Str(Json.Get(me,"username"));
            byte[] bytes=new byte[12];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);
            challenge=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant(); challengeExpires=DateTime.UtcNow.AddMinutes(10);
            string command="/start "+challenge;
            pairStatus.Text="Open @"+pendingBot+" on your phone and send:\n"+command+"\nThen click Finish linking within 10 minutes. The command has been copied to your clipboard.";
            try {Clipboard.SetText(command);} catch { }
            finish.Enabled=true;
        } catch(Exception e) {pairStatus.Text=SafeError(e);}
          finally {pairing=false;link.Enabled=true;}
    }
    async Task FinishPair() {
        if(pairing)return;pairing=true;finish.Enabled=false;link.Enabled=false;
        try {
            if(DateTime.UtcNow>challengeExpires)throw new InvalidOperationException("The pairing code expired. Click Prepare Telegram link again.");
            var updates=await telegram.Call(pendingToken,"getUpdates",new Dictionary<string,string>{{"timeout","0"},{"limit","100"},{"allowed_updates","[\"message\"]"}});
            string chat=Pairing.FindChat(updates,challenge);
            if(String.IsNullOrEmpty(chat))throw new InvalidOperationException("Pairing message not found. Send the exact displayed command to @"+pendingBot+" in a private chat, then click Finish linking again. Another program must not be polling this bot.");
            settings.Token=pendingToken;settings.ChatId=chat;settings.BotName=pendingBot;SaveSettings();
            challenge="";pairStatus.Text="Telegram linked. Click Send test alert and confirm it arrives on your phone.";
            nextTelegram=DateTime.MinValue;telegramFailures=0;UpdatePhoneStatus();
        } catch(Exception e) {pairStatus.Text=SafeError(e);finish.Enabled=DateTime.UtcNow<=challengeExpires;}
          finally {pairing=false;link.Enabled=true;}
    }
    async Task TestAlert() {
        test.Enabled=false;
        try {
            LocalAlert("Test alert: Codex Usage Watch is running. This does not change your usage or redeem a reset.");
            if(String.IsNullOrEmpty(settings.ChatId)) {phoneStatus.Text="Windows test sent. Link Telegram above to test your phone.";return;}
            await telegram.Send(settings,"Test alert: Codex Usage Watch is connected. You will receive an alert when your weekly Codex allowance is 2% or less remaining. No reset was used.");
            phoneStatus.Text="Telegram test accepted at "+DateTime.Now.ToString("h:mm:ss tt")+". Confirm arrival and sound on your phone.";
        } catch(Exception e) {phoneStatus.Text="Test: "+SafeError(e);}
          finally {test.Enabled=true;}
    }
    void ChooseCodex() {
        using(var picker=new OpenFileDialog { Filter="Codex executable|codex.exe",Title="Choose Codex CLI executable" }) {
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            if(reading) {MessageBox.Show(this,"A usage read is in progress. Try again in a moment.",Text);return;}
            settings.CodexPath=picker.FileName;SaveSettings();if(codex!=null)codex.Dispose();codex=null;nextPoll=DateTime.MinValue;
        }
    }
    public static void SetStartup(bool enabled) {
        if(!enabled) {if(File.Exists(StartupPath))File.Delete(StartupPath);return;}
        Type type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);
        dynamic shortcut=shell.CreateShortcut(StartupPath);shortcut.TargetPath=Application.ExecutablePath;shortcut.Arguments="--background";
        shortcut.WorkingDirectory=Path.GetDirectoryName(Application.ExecutablePath);shortcut.Description="Codex weekly usage alerts";shortcut.WindowStyle=7;shortcut.Save();
    }
}
public static class Program {
    [STAThread] public static void Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Contains("--probe")) { Probe(args);return; }
        bool created;
        using(var mutex=new Mutex(true,"Local\\CodexUsageWatch-"+Environment.UserName,out created)) {
            if(!created) {MessageBox.Show("Codex Usage Watch is already running. Open it from its tray icon.","Codex Usage Watch");return;}
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new WatchForm(!args.Contains("--background")));
        }
    }
    static void Probe(string[] args) {
        string output=args.SkipWhile(a=>a!="--probe").Skip(1).FirstOrDefault();
        if(String.IsNullOrEmpty(output))return;
        try {
            Directory.CreateDirectory(Storage.Root);
            using(var c=new CodexClient("")) {
                var u=Task.Run(()=>c.Read()).GetAwaiter().GetResult();
                Storage.Write("probe-credential-test.dat",new Settings {Token="test-only"},true);
                bool encryptedRoundTrip=Storage.Read<Settings>("probe-credential-test.dat",true).Token=="test-only";
                File.Delete(Path.Combine(Storage.Root,"probe-credential-test.dat"));
                File.WriteAllText(output,Json.Dump(new {ok=true,remaining=u.Remaining,weeklyReset=u.Reset,encryptedRoundTrip=encryptedRoundTrip}));
            }
        } catch(Exception e) {File.WriteAllText(output,Json.Dump(new {ok=false,error=e.GetType().Name}));Environment.ExitCode=1;}
    }
}
}
