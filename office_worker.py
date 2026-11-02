"""Private, local-only Office worker. Requests arrive only through inherited stdin."""
import ctypes, json, os, subprocess, sys, time, uuid, shutil, zipfile, threading
from xml.dom import minidom
from ctypes import wintypes as W

def own_process_tree():
    # Closing this worker's non-inherited job handle also terminates its children.
    class BASIC(ctypes.Structure):
        _fields_=[('times',ctypes.c_longlong*2),('flags',W.DWORD),('min',ctypes.c_size_t),('max',ctypes.c_size_t),('active',W.DWORD),('affinity',ctypes.c_size_t),('priority',W.DWORD),('schedule',W.DWORD)]
    class LIMIT(ctypes.Structure):
        _fields_=[('basic',BASIC),('io',ctypes.c_ulonglong*6),('memory',ctypes.c_size_t*4)]
    k=ctypes.WinDLL('kernel32',use_last_error=True)
    k.CreateJobObjectW.restype=W.HANDLE
    k.CreateJobObjectW.argtypes=[ctypes.c_void_p,W.LPCWSTR]
    k.SetInformationJobObject.argtypes=[W.HANDLE,ctypes.c_int,ctypes.c_void_p,W.DWORD]
    k.AssignProcessToJobObject.argtypes=[W.HANDLE,W.HANDLE]
    k.GetCurrentProcess.restype=W.HANDLE
    job=k.CreateJobObjectW(None,None); limit=LIMIT();limit.basic.flags=0x2000
    if not job or not k.SetInformationJobObject(job,9,ctypes.byref(limit),ctypes.sizeof(limit)) or not k.AssignProcessToJobObject(job,k.GetCurrentProcess()):
        raise ctypes.WinError(ctypes.get_last_error())
    return job

job=own_process_tree()
program,profile,cache=sys.argv[1:4]
if len(sys.argv)>4:
    parent_id=int(sys.argv[4])
    def watch_parent():
        kernel=ctypes.WinDLL('kernel32',use_last_error=True)
        kernel.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD];kernel.OpenProcess.restype=W.HANDLE
        kernel.WaitForSingleObject.argtypes=[W.HANDLE,W.DWORD]
        parent=kernel.OpenProcess(0x00100000,False,parent_id)
        if parent:kernel.WaitForSingleObject(parent,0xFFFFFFFF)
        os._exit(0)
    threading.Thread(target=watch_parent,daemon=True).start()
os.environ['SAL_SKIA']='raster'
os.environ['SAL_DISABLE_OPENCL']='1'
os.environ['SAL_DISABLE_PRINTERLIST']='1'
os.environ['SAL_DISABLE_DEFAULTPRINTER']='1'
os.environ['SAL_DISABLE_SYNCHRONOUS_PRINTER_DETECTION']='1'
dll_directory=os.add_dll_directory(program)
import uno, unohelper
from com.sun.star.beans import PropertyValue
from com.sun.star.task import XInteractionHandler
from com.sun.star.document.MacroExecMode import NEVER_EXECUTE
from com.sun.star.document.UpdateDocMode import NO_UPDATE

class RejectPrompts(unohelper.Base,XInteractionHandler):
    def handle(self,request):
        for continuation in request.getContinuations():
            if hasattr(continuation,'abort'):
                continuation.abort();return
            try:
                abort=continuation.queryInterface(uno.getTypeByName('com.sun.star.task.XInteractionAbort'))
                if abort:abort.select();return
            except Exception:pass

def prop(name,value):
    p=PropertyValue();p.Name=name;p.Value=value;return p

def reply(value):
    print(json.dumps(value,ensure_ascii=True),flush=True)

def fix_pdf_layout(output):
    # PDF importer coordinates are page-relative. Writer's DOCX exporter otherwise
    # adds margins and can put imported text behind opaque background shapes.
    fixed=output+'.layout'
    with zipfile.ZipFile(output) as source,zipfile.ZipFile(fixed,'w',zipfile.ZIP_DEFLATED) as dest:
        for name in source.namelist():
            data=source.read(name)
            if name=='word/document.xml':
                xml=minidom.parseString(data)
                for item in xml.getElementsByTagName('wp:anchor'):item.setAttribute('behindDoc','0')
                for tag in ('wp:positionH','wp:positionV'):
                    for item in xml.getElementsByTagName(tag):item.setAttribute('relativeFrom','page')
                for item in xml.getElementsByTagName('v:shape'):
                    layer='1' if item.getElementsByTagName('v:textbox') else '-1'
                    item.setAttribute('style',item.getAttribute('style')+';mso-position-horizontal-relative:page;mso-position-vertical-relative:page;z-index:'+layer)
                for item in xml.getElementsByTagName('w:pgMar'):
                    for side in ('left','right','top','bottom'):item.setAttribute('w:'+side,'0')
                data=xml.toxml(encoding='UTF-8')
                xml.unlink()
            dest.writestr(name,data)
    os.replace(fixed,output)

pipe='DropShift'+uuid.uuid4().hex
server_log=open(os.path.join(profile,'worker-engine.log'),'ab',buffering=0)
server=subprocess.Popen([os.path.join(program,'soffice.exe'),'-env:UserInstallation='+uno.systemPathToFileUrl(profile),'--headless','--nologo','--nodefault','--norestore','--accept=pipe,name='+pipe+';urp;StarOffice.ComponentContext'],stdin=subprocess.DEVNULL,stdout=server_log,stderr=server_log,creationflags=0x08000000)
local=uno.getComponentContext()
resolver=local.ServiceManager.createInstanceWithContext('com.sun.star.bridge.UnoUrlResolver',local)
deadline=time.monotonic()+90
while True:
    try:
        context=resolver.resolve('uno:pipe,name='+pipe+';urp;StarOffice.ComponentContext');break
    except Exception:
        if time.monotonic()>deadline:raise RuntimeError('Office startup timed out')
        time.sleep(.1)
desktop=context.ServiceManager.createInstanceWithContext('com.sun.star.frame.Desktop',context)
options=(prop('Hidden',True),prop('ReadOnly',True),prop('MacroExecutionMode',NEVER_EXECUTE),prop('UpdateDocMode',NO_UPDATE),prop('InteractionHandler',RejectPrompts()))
# The private pipe keeps Office alive; only load modules needed by actual inputs.
anchors=[]
reply({'ok':True,'ready':True})
documents=[]
retained_bytes=0
for line in sys.stdin:
    doc=None
    try:
        request=json.loads(line);source=request['source'];target=request['target'];output=request['output']
        opts=options
        extension=os.path.splitext(source)[1].lower()
        if source.lower().endswith('.pdf'):opts+=(prop('FilterName','writer_pdf_import'),)
        if source.lower().endswith(('.csv','.tsv')):opts+=(prop('FilterName','Text - txt - csv (StarCalc)'),prop('FilterOptions',('9' if source.lower().endswith('.tsv') else '44')+',34,76,1'),)
        if os.path.getsize(source)>200*1024*1024:raise ValueError('Office input exceeds the 200 MB limit')
        cached_folder=os.path.join(cache,uuid.uuid4().hex)
        os.makedirs(cached_folder)
        cached_source=os.path.join(cached_folder,os.path.basename(source))
        shutil.copyfile(source,cached_source)
        doc=desktop.loadComponentFromURL(uno.systemPathToFileUrl(cached_source),'_blank',0,opts)
        if doc is None:raise ValueError('Document is encrypted, damaged or unsupported')
        if extension in ('.csv','.tsv'):
            styles=doc.getStyleFamilies().getByName('PageStyles')
            for name in styles.getElementNames():
                style=styles.getByName(name)
                style.HeaderIsOn=False
                style.FooterIsOn=False
        if target=='PDF':
            filter_name='calc_pdf_Export' if doc.supportsService('com.sun.star.sheet.SpreadsheetDocument') else 'impress_pdf_Export' if doc.supportsService('com.sun.star.presentation.PresentationDocument') else 'writer_pdf_Export'
        else:filter_name={'DOCX':'Office Open XML Text','ODT':'writer8','XLSX':'Calc MS Excel 2007 XML','ODS':'calc8','PPTX':'Impress MS PowerPoint 2007 XML','ODP':'impress8'}[target]
        doc.storeToURL(uno.systemPathToFileUrl(output),(prop('FilterName',filter_name),prop('Overwrite',False)))
        if extension=='.pdf' and target=='DOCX':fix_pdf_layout(output)
        documents.append(doc);retained_bytes+=os.path.getsize(cached_source);doc=None
        response={'ok':True}
    except Exception as error:response={'ok':False,'error':str(error),'restart': 'bridge' in str(error).lower() or 'disposed' in str(error).lower()}
    finally:
        if doc is not None:
            try:doc.close(True)
            except Exception:
                try:doc.dispose()
                except Exception:pass
    # Only private cache copies remain open; the completed output is closed.
    response['retire']=len(documents)>=12 or retained_bytes>=64*1024*1024
    reply(response)
    # Avoid this engine version's deferred document-close crashes. Keep a bounded
    # set of private cached documents, then retire the whole owned process tree.
    # C# removes this session cache after all owned processes have exited.
    if len(documents)>=12 or retained_bytes>=64*1024*1024:break
# EOF or retirement closes the job handle and terminates all owned processes.
os._exit(0)
