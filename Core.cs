using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexUsageWatch {
public static class Json {
    public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
    public static Dictionary<string, object> Map(object v) { return v as Dictionary<string, object>; }
    public static object Get(object v, string key) { var m=Map(v); return m!=null && m.ContainsKey(key)?m[key]:null; }
    public static string Str(object v) { return v==null?"":Convert.ToString(v,CultureInfo.InvariantCulture); }
    public static double Num(object v) { return Convert.ToDouble(v,CultureInfo.InvariantCulture); }
    public static Dictionary<string, object> Parse(string s) { return Serializer.DeserializeObject(s) as Dictionary<string,object>; }
    public static string Dump(object v) { return Serializer.Serialize(v); }
}
public sealed class WeeklyUsage {
    public double Used;
    public long Reset;
    public string Bucket;
    public double Remaining { get { return 100-Used; } }
    public string Key { get { return Bucket+":"+Reset; } }
    public static WeeklyUsage Parse(object result) {
        object bucket=null; var buckets=Json.Map(Json.Get(result,"rateLimitsByLimitId"));
        if(buckets!=null && buckets.ContainsKey("codex")) bucket=buckets["codex"];
        else { var fallback=Json.Get(result,"rateLimits"); var id=Json.Str(Json.Get(fallback,"limitId")); if(id=="codex" || id=="") bucket=fallback; }
        foreach(var field in new[]{"primary","secondary"}) {
            var w=Json.Get(bucket,field);
            if(w==null || Json.Get(w,"windowDurationMins")==null || Json.Num(Json.Get(w,"windowDurationMins"))!=10080) continue;
            if(Json.Get(w,"usedPercent")==null || Json.Get(w,"resetsAt")==null) continue;
            double used=Json.Num(Json.Get(w,"usedPercent")); long reset=Convert.ToInt64(Json.Get(w,"resetsAt"),CultureInfo.InvariantCulture);
            if(Double.IsNaN(used) || Double.IsInfinity(used) || used<0 || used>100 || reset<=0) continue;
            return new WeeklyUsage { Used=used, Reset=reset, Bucket="codex" };
        }
        throw new InvalidOperationException("Codex did not return a valid weekly usage window. Check your active account in Codex.");
    }
}
public sealed class AlertState {
    public string WindowKey="";
    public double LastUsed=-1;
    public bool LocalSent;
    public bool TelegramSent;
    public void Observe(WeeklyUsage u) {
        if(WindowKey!=u.Key || (LastUsed>=98 && u.Used<98)) { LocalSent=false; TelegramSent=false; }
        WindowKey=u.Key; LastUsed=u.Used;
    }
    public bool NeedsLocal(WeeklyUsage u) { return u.Remaining<=2 && !LocalSent; }
    public bool NeedsTelegram(WeeklyUsage u) { return u.Remaining<=2 && !TelegramSent; }
}
public static class Schedule {
    public static int PollSeconds(WeeklyUsage u, int failures) { if(failures>0) return Math.Min(300,5*(1<<Math.Min(failures,6))); return u!=null && u.Remaining<=10?5:30; }
}
public static class Pairing {
    public static string FindChat(object updates, string code) {
        var rows=updates as IEnumerable;
        if(rows==null) return null;
        foreach(var row in rows) {
            var m=Json.Get(row,"message"); var chat=Json.Get(m,"chat");
            if(Json.Str(Json.Get(chat,"type"))=="private" && Json.Str(Json.Get(m,"text"))=="/start "+code) return Json.Str(Json.Get(chat,"id"));
        }
        return null;
    }
}
}
