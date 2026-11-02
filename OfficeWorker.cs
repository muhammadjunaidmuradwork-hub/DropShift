using System;using System.IO;using System.Diagnostics;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Collections.Generic;using System.Security.Cryptography;using System.Text;using System.Threading;
namespace DropShift {
 static class OfficeWorker {
  sealed class Disconnected:IOException {public Disconnected(string message):base(message){}}
  sealed class Session:IDisposable {
   public Process Process;public string Cache;public FileStream Lease;public int Jobs;public long Bytes;
   public void Dispose(){
    try{if(Process!=null){if(!Process.HasExited){Process.Kill();Process.WaitForExit(5000);}Process.Dispose();}}catch(Exception ex){Log.Write("Office shutdown: "+ex.Message);}
    if(Cache!=null){bool removed=false;for(int i=0;i<30;i++){try{if(Directory.Exists(Cache))Directory.Delete(Cache,true);removed=true;break;}catch(IOException){Thread.Sleep(100);}catch(UnauthorizedAccessException){Thread.Sleep(100);}}if(!removed)Log.Write("Office cache cleanup deferred: "+Cache);}
    if(Lease!=null)Lease.Dispose();
   }
  }
  static readonly object Gate=new object(),StateGate=new object();static Session active;static Task<Session> standby;static bool stopped;
  static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  static OfficeWorker(){AppDomain.CurrentDomain.ProcessExit+=delegate{Stop();};}
  public static void WarmUp(){lock(StateGate){if(stopped||active!=null||standby!=null)return;standby=Task.Run((Func<Session>)Create);}}
  static void PrepareReplacement(){lock(StateGate){if(!stopped&&standby==null)standby=Task.Run((Func<Session>)Create);}}
  public static void Stop(){
   Session current;Task<Session> next;
   lock(StateGate){stopped=true;current=active;active=null;next=standby;standby=null;}
   if(current!=null)current.Dispose();
   if(next!=null){if(next.Status==TaskStatus.RanToCompletion)next.Result.Dispose();else next.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();else if(t.IsFaulted){var ignored=t.Exception;}});}
  }
  static Dictionary<string,object> Read(Session session,int timeout){
   var read=session.Process.StandardOutput.ReadLineAsync();if(!read.Wait(timeout))throw new IOException("Office conversion timed out. Original kept.");
   string line=read.Result;if(line==null)throw new Disconnected("Office worker stopped unexpectedly. Original kept.");
   var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
   if(!(bool)result["ok"]){if(result.ContainsKey("restart")&&(bool)result["restart"])throw new Disconnected("Office worker connection lost: "+result["error"]);throw new InvalidDataException("Office conversion failed: "+result["error"]);}
   return result;
  }
  static Session Create(){
   var session=new Session();var timer=Stopwatch.StartNew();
   try{
    string program=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Office","program");
    if(!Directory.Exists(program))throw new IOException("Office engine is missing. Extract the entire app package.");
    string[] cores=Directory.GetDirectories(program,"python-core-*");if(cores.Length==0)throw new IOException("Office Python runtime is missing.");
    string core=cores[0],python=Path.Combine(core,"bin","python.exe"),identity;
    using(var sha=SHA256.Create())identity=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Process.GetCurrentProcess().MainModule.FileName))).Replace("-","").Substring(0,12);
    string root=Path.Combine(Log.Folder,"OfficeProfile032-"+identity),profile=null;
    // OS file leases prevent different workers from sharing an Office profile.
    for(int i=0;i<8;i++){
     string candidate=Path.Combine(root,"Slot-"+i);Directory.CreateDirectory(candidate);
     try{session.Lease=new FileStream(Path.Combine(candidate,"worker.lease"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);profile=candidate;break;}catch(IOException){}
    }
    if(profile==null)throw new IOException("Office workers are busy. Try again shortly.");
    foreach(string abandoned in Directory.GetDirectories(profile,"Session-*")){try{Directory.Delete(abandoned,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    Directory.CreateDirectory(Path.Combine(profile,"user"));string config=Path.Combine(profile,"user","registrymodifications.xcu");
    if(!File.Exists(config))File.WriteAllText(config,"<?xml version=\"1.0\"?><oor:items xmlns:oor=\"http://openoffice.org/2001/registry\"><item oor:path=\"/org.openoffice.Office.Common/Security/Scripting\"><prop oor:name=\"MacroSecurityLevel\" oor:op=\"fuse\"><value>3</value></prop></item></oor:items>");
    session.Cache=Path.Combine(profile,"Session-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session.Cache);
    var info=new ProcessStartInfo(python,"-u \""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"office_worker.py")+"\" \""+program+"\" \""+profile+"\" \""+session.Cache+"\" "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    info.EnvironmentVariables["PYTHONHOME"]=core;info.EnvironmentVariables["PYTHONPATH"]=program;info.EnvironmentVariables["PATH"]=program+";"+Environment.GetEnvironmentVariable("PATH");
    info.EnvironmentVariables["URE_BOOTSTRAP"]="vnd.sun.star.pathname:"+Path.Combine(program,"fundamental.ini");info.EnvironmentVariables["UNO_PATH"]=program;
    session.Process=new Process{StartInfo=info};session.Process.Start();session.Process.StandardError.ReadToEndAsync();Read(session,100000);
    Log.Write("Office worker ready in "+timer.Elapsed.TotalSeconds.ToString("F2")+" seconds");return session;
   }catch{session.Dispose();throw;}
  }
  static Session Get(){
   Task<Session> next;
   lock(StateGate){if(stopped)throw new IOException("DropShift is shutting down.");if(active!=null)return active;next=standby;standby=null;}
   Session current;
   if(next==null)current=Create();else try{current=next.GetAwaiter().GetResult();}catch(Exception ex){Log.Write("Office prewarm failed; retrying: "+ex.Message);current=Create();}
   lock(StateGate){if(stopped){current.Dispose();throw new IOException("DropShift is shutting down.");}active=current;}
   return current;
  }
  static void Retire(Session session){lock(StateGate){if(active==session)active=null;}session.Dispose();}
  public static void Convert(string source,string target,string output){lock(Gate){
   for(int attempt=0;attempt<2;attempt++){
    Session session=null;
    try{
     Formats.Report("Preparing Office conversion…");session=Get();Formats.Report("Converting Office document…");
     if(session.Process.HasExited)throw new Disconnected("Office worker exited between jobs.");
     try{session.Process.StandardInput.WriteLine(Json.Serialize(new{source=source,target=target,output=output}));session.Process.StandardInput.Flush();}catch(IOException ex){throw new Disconnected(ex.Message);}
     var result=Read(session,600000);session.Jobs++;session.Bytes+=new FileInfo(source).Length;
     if(result.ContainsKey("retire")&&(bool)result["retire"]){Retire(session);PrepareReplacement();}
     else if(session.Jobs>=1)PrepareReplacement();
     return;
    }catch(Disconnected){if(session!=null)Retire(session);if(attempt!=0)throw;if(File.Exists(output))File.Delete(output);Log.Write("Restarting Office worker after lost connection");}
    catch{if(session!=null)Retire(session);throw;}
   }
  }}
 }
}
