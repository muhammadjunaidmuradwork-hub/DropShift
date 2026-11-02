using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Text;using System.Threading.Tasks;
namespace DropShift {
 static class MediaEngine {
  static readonly string[] Audio={"MP3","WAV","FLAC","OGG","M4A","AAC","WMA","OPUS","AIFF"};
  static readonly string[] Video={"MP4","MOV","MKV","AVI","WEBM","M4V","WMV","MPEG","MPG"};
  public static string Format(string path){string f=Path.GetExtension(path).TrimStart('.').ToUpperInvariant();return Audio.Contains(f)||Video.Contains(f)?f:null;}
  public static string[] Targets(string path){string f=Format(path);if(f==null)return new string[0];return (Audio.Contains(f)?new[]{"MP3","WAV","FLAC","OGG","M4A"}:new[]{"MP4","MKV","WEBM","MP3","WAV"}).Where(t=>t!=f).ToArray();}
  static string Q(string s){return "\""+s+"\"";}
  static void Run(string args,int timeout){
   string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Codecs","ffmpeg.exe");if(!File.Exists(exe))throw new IOException("The media engine is missing. Extract the complete app package.");
   using(var p=new Process{StartInfo=new ProcessStartInfo(exe,"-hide_banner -nostdin -loglevel error "+args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){p.Start();var err=p.StandardError.ReadToEndAsync();var stdout=p.StandardOutput.ReadToEndAsync();if(!p.WaitForExit(timeout)){p.Kill();p.WaitForExit();throw new IOException("Media conversion exceeded the 30-minute limit. Try a shorter file.");}Task.WaitAll(err,stdout);if(p.ExitCode!=0)throw new InvalidDataException("Media conversion failed: "+err.Result.Trim());}
  }
  public static string Convert(string source,string target){
   if(!Targets(source).Contains(target))throw new InvalidDataException("Unsupported media conversion.");
   string folder=Path.Combine(Path.GetDirectoryName(source),".dropshift-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   try{using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)){
    string output=Path.Combine(folder,"result."+target.ToLowerInvariant());string options;
    switch(target){case "MP3":options="-map 0:a:0 -vn -c:a libmp3lame -q:a 2";break;case "WAV":options="-map 0:a:0 -vn -c:a pcm_s16le";break;case "FLAC":options="-map 0:a:0 -vn -c:a flac";break;case "OGG":options="-map 0:a:0 -vn -c:a libvorbis -q:a 5";break;case "M4A":options="-map 0:a:0 -vn -c:a aac -b:a 192k";break;case "WEBM":options="-map 0:v:0 -map 0:a:0? -c:v libvpx-vp9 -crf 32 -b:v 0 -deadline realtime -cpu-used 4 -c:a libopus";break;default:options="-map 0:v:0 -map 0:a:0? -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 192k"+(target=="MP4"?" -movflags +faststart":"");break;}
    Run("-n -protocol_whitelist file -i "+Q(source)+" "+options+" -map_metadata -1 "+Q(output),1800000);
    if(!File.Exists(output)||new FileInfo(output).Length==0)throw new InvalidDataException("The media output is empty.");
    Run("-xerror -protocol_whitelist file -i "+Q(output)+" -map 0 -f null -",1800000);
    return Formats.Publish(output,source,"."+target.ToLowerInvariant(),false);
   }}finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
  }
 }
}
