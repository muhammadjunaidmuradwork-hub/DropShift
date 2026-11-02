using System;using System.IO;using System.Text;using System.Web.Script.Serialization;using Tesseract;
// Separate native OCR process: a timeout or decoder crash cannot terminate the app.
static class OcrWorker {
 static int Main(string[] args){
  try{
   if(args.Length<2)throw new ArgumentException("Expected input PNG and output JSON.");
   if(args.Length>2){int parentId=int.Parse(args[2]);System.Threading.ThreadPool.QueueUserWorkItem(delegate{try{using(var parent=System.Diagnostics.Process.GetProcessById(parentId))parent.WaitForExit();}catch(ArgumentException){}Environment.Exit(2);});}
   Environment.SetEnvironmentVariable("OMP_THREAD_LIMIT","2");
   using(var engine=new TesseractEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tessdata"),"eng",EngineMode.LstmOnly))
   using(var pix=Pix.LoadFromFile(args[0]))
   using(var page=engine.Process(pix,PageSegMode.AutoOsd)){
    File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{text=page.GetText(),confidence=page.GetMeanConfidence()}),new UTF8Encoding(false));
   }
   return 0;
  }catch(Exception ex){Console.Error.WriteLine("OCR failed: "+ex.Message);return 1;}
 }
}
