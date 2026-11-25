using System;
using CodexUsageWatch;
class Tests {
 static int count=0;
 static void Check(bool v,string name) { if(!v) throw new Exception("FAIL: "+name); count++; Console.WriteLine("PASS: "+name); }
 static WeeklyUsage Usage(double used,long reset=1000) { return new WeeklyUsage { Used=used,Reset=reset,Bucket="codex" }; }
 static void Main() {
    var s=new AlertState(); s.Observe(Usage(97)); Check(!s.NeedsLocal(Usage(97)),"no early alert at 3% remaining");
    s.Observe(Usage(98)); Check(s.NeedsLocal(Usage(98)),"alert at exactly 2% remaining");
    s.LocalSent=true; Check(!s.NeedsLocal(Usage(98)) && s.NeedsTelegram(Usage(98)),"delivery channels are independent");
    s.TelegramSent=true; s.Observe(Usage(100)); Check(!s.NeedsLocal(Usage(100)),"no repeat as allowance falls further");
    s=Json.Serializer.Deserialize<AlertState>(Json.Dump(s)); s.Observe(Usage(100)); Check(!s.NeedsTelegram(Usage(100)),"deduplication survives serialization and restart");
    s.Observe(Usage(99)); Check(!s.NeedsLocal(Usage(99)) && !s.NeedsTelegram(Usage(99)),"minor correction within low band does not repeat alerts");
    s.Observe(Usage(1)); s.Observe(Usage(99)); Check(s.NeedsTelegram(Usage(99)),"banked reset rearms in same weekly window");
    s.LocalSent=true; s.TelegramSent=true; s.Observe(Usage(99,2000)); Check(s.NeedsLocal(Usage(99,2000)),"weekly renewal rearms even when first observed low");
    s=new AlertState(); s.Observe(Usage(100)); Check(s.NeedsLocal(Usage(100)),"startup below threshold alerts immediately");
    var p=WeeklyUsage.Parse(Json.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":98,\"windowDurationMins\":10080,\"resetsAt\":1000}}}"));
    Check(p.Remaining==2,"weekly window can be primary");
    p=WeeklyUsage.Parse(Json.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":100,\"windowDurationMins\":300,\"resetsAt\":1000},\"secondary\":{\"usedPercent\":12,\"windowDurationMins\":10080,\"resetsAt\":2000}}},\"rateLimits\":{\"limitId\":\"other\"}}"));
    Check(p.Remaining==88 && p.Reset==2000,"prefer core bucket and secondary weekly window over five-hour usage");
    foreach(var data in new[]{"{}","{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":99,\"windowDurationMins\":10080,\"resetsAt\":1000}}}","{\"rateLimits\":{\"primary\":{\"usedPercent\":101,\"windowDurationMins\":10080,\"resetsAt\":1000}}}","{\"rateLimits\":{\"primary\":{\"windowDurationMins\":10080,\"resetsAt\":1000}}}"}) {
      bool rejected=false; try { WeeklyUsage.Parse(Json.Parse(data)); } catch { rejected=true; } Check(rejected,"reject absent, wrong-bucket or invalid usage");
    }
    Check(Schedule.PollSeconds(Usage(98),0)==5 && Schedule.PollSeconds(Usage(2),0)==30,"adaptive polling");
    Check(Schedule.PollSeconds(null,20)==300,"bounded error backoff");
    var updates=Json.Get(Json.Parse("{\"result\":[{\"message\":{\"chat\":{\"id\":1,\"type\":\"group\"},\"text\":\"/start code\"}},{\"message\":{\"chat\":{\"id\":2,\"type\":\"private\"},\"text\":\"/start wrong\"}},{\"message\":{\"chat\":{\"id\":3,\"type\":\"private\"},\"text\":\"/start code\"}}]}"),"result");
    Check(Pairing.FindChat(updates,"code")=="3","pair only private chat with exact challenge");
    Check(Pairing.FindChat(updates,"different")==null,"reject unmatched pairing challenge");
    Console.WriteLine(count+" checks passed.");
 }
}
