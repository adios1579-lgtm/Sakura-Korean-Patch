using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// SAKURA～설월화～ 한글패치 업데이트 엔진.
// Canvas3 한글패치의 엔진 구조를 바탕으로 앱 ID·허용 경로·실행 파일·보호 폴더를 이 게임에 맞게 정했다.
namespace SakuraUpdate {
 public class Settings { public string repository {get;set;} public string app_id {get;set;} }
 public class Item { public string path {get;set;} public string sha256 {get;set;} public long size {get;set;} public string package {get;set;} }
 public class Package { public string name {get;set;} public string sha256 {get;set;} public long size {get;set;} }
 public class State { public int schema {get;set;} public string app_id {get;set;} public string version {get;set;} public string base_version {get;set;} public List<Item> files {get;set;} }
 public class Manifest { public int schema {get;set;} public string app_id {get;set;} public string version {get;set;} public string base_version {get;set;} public string release_tag {get;set;} public string notes {get;set;} public List<Item> files {get;set;} public List<Item> inventory {get;set;} public List<string> deleted {get;set;} public List<Package> packages {get;set;} }
 public class Change { public string path {get;set;} public bool existed {get;set;} }
 public class Journal { public bool committed {get;set;} public List<Change> changes {get;set;} }
 public class BaseInfo { public int schema {get;set;} public string app_id {get;set;} public List<Item> files {get;set;} }
 public class Stamp { public long size {get;set;} public long ticks {get;set;} }
 public class BaseCache { public string requirements {get;set;} public Dictionary<string,Stamp> files {get;set;} }

 public static class Engine {
  public const string AppId="sakura-ko";
  public const string GameExe="SAKURA.EXE";
  public const string Title="SAKURA～설월화～";
  const string WorkName=".sakura-update";
  static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024, RecursionLimit=64 }; }
  public static T Parse<T>(string s) { return Serializer().Deserialize<T>(s); }
  public static string Json(object v) { return Serializer().Serialize(v); }
  public static string Hash(string file) { using(var s=File.OpenRead(file)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
  public static string TextHash(string s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-",""); }
  static bool Digest(string h) { return h!=null && Regex.IsMatch(h,"^[a-f0-9]{64}$"); }
  // 업데이트가 건드릴 수 있는 경로만 통과시킨다.
  //  - 한글패치가 관리하는 것: 실행 파일, 전용 글꼴, 대본(AdvData/MES), 그림(AdvData/GRP), 설명서, 엔진 DLL
  //  - 세이브(AdvData/SaveData)와 음성·음악·효과음·동영상·DAT 은 관리 대상이 아니다.
  //    baseCheck 일 때만 본편 확인용으로 음악·효과음·DAT 을 읽을 수 있다(쓰기 경로로는 쓰이지 않는다).
  public static string Safe(string root,string rel,bool internalState=false,bool baseCheck=false) {
   if(String.IsNullOrEmpty(rel)||rel.Length>220||rel.Contains("\\")||rel.Contains(":")||rel.StartsWith("/")||rel.EndsWith("/")) throw new Exception("잘못된 업데이트 경로입니다.");
   var parts=rel.Split('/');
   foreach(var p in parts) if(p=="."||p==".."||p.TrimEnd(' ','.')!=p||p.Length==0||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(p,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)",RegexOptions.IgnoreCase)) throw new Exception("허용되지 않는 업데이트 경로입니다.");
   bool allowed=false;
   if(internalState && rel=="updates/installed.json") allowed=true;
   if(parts.Length==1) allowed=new[]{GameExe,"SkrKR034.ttf","SkrKR034_NOTICE.txt","사용설명서.txt"}.Contains(rel,StringComparer.OrdinalIgnoreCase);
   if(Regex.IsMatch(rel,"^AdvData/MES/[A-Za-z0-9_]+\\.mes$",RegexOptions.IgnoreCase)) allowed=true;
   if(Regex.IsMatch(rel,"^AdvData/GRP/(battle|BG|bst|SYSTEM)/[A-Za-z0-9_]+\\.CRX$",RegexOptions.IgnoreCase)) allowed=true;
   if(rel=="updates/SakuraUpdater.dll"||rel=="updates/base-requirements.json") allowed=true;
   if(baseCheck && Regex.IsMatch(rel,"^AdvData/(MUSIC|SE|DAT)/[A-Za-z0-9_]+\\.(pcm|dat)$",RegexOptions.IgnoreCase)) allowed=true;
   if(parts.Length>=2 && parts[0].Equals("AdvData",StringComparison.OrdinalIgnoreCase) && parts[1].Equals("SaveData",StringComparison.OrdinalIgnoreCase)) allowed=false;
   if(!allowed) throw new Exception("업데이트가 보호 영역을 포함합니다: "+rel);
   string full=Path.GetFullPath(Path.Combine(root,rel.Replace('/',Path.DirectorySeparatorChar)));
   string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new Exception("설치 폴더 밖의 경로입니다.");
   string check=full;
   while(check!=null && check.Length>=prefix.Length-1) {
    if((File.Exists(check)||Directory.Exists(check)) && (File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0) throw new Exception("연결된 폴더/파일에는 업데이트할 수 없습니다: "+rel);
    check=Path.GetDirectoryName(check);
   }
   return full;
  }
  public static Version Ver(string s) { Version v; if(s==null||!Regex.IsMatch(s,"^[0-9]+\\.[0-9]+\\.[0-9]+$")||!Version.TryParse(s,out v)) throw new Exception("버전 형식이 잘못되었습니다."); return v; }
  public static void Validate(Manifest m,State local,string root) {
   if(m==null||m.schema!=1||m.app_id!=AppId||local==null||local.app_id!=AppId||local.files==null||m.files==null||m.inventory==null||m.packages==null||m.deleted==null) throw new Exception("지원하지 않는 업데이트 정보입니다.");
   Ver(m.version);Ver(m.base_version);Ver(local.version);
   if(m.base_version!=local.base_version || Ver(local.version)<Ver(m.base_version)) throw new Exception("이 설치본과 업데이트 기준 버전이 다릅니다. 최신 전체 배포본을 사용해 주세요.");
   if(m.release_tag!="v"+m.version) throw new Exception("릴리스 태그가 버전과 다릅니다.");
   var inv=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in m.inventory) { Safe(root,f.path); if(!Digest(f.sha256)||f.size<0||f.size>2L*1024*1024*1024||inv.ContainsKey(f.path)) throw new Exception("파일 목록에 중복/형식 오류가 있습니다."); inv.Add(f.path,f); }
   var pk=new Dictionary<string,Package>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in m.packages) { if(p.name==null||!Regex.IsMatch(p.name,"^sakura-update-[A-Za-z0-9.-]+\\.zip$")||!Digest(p.sha256)||p.size<=0||p.size>=2L*1024*1024*1024||pk.ContainsKey(p.name)) throw new Exception("업데이트 묶음 정보가 잘못되었습니다."); pk.Add(p.name,p); }
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var f in m.files) { Safe(root,f.path); Item i; if(!seen.Add(f.path)||!inv.TryGetValue(f.path,out i)||i.sha256!=f.sha256||i.size!=f.size||f.package==null||!pk.ContainsKey(f.package)) throw new Exception("변경 파일 대응 오류입니다."); }
   foreach(var n in m.deleted) { Safe(root,n); if(inv.ContainsKey(n)||!seen.Add(n)) throw new Exception("삭제 목록이 변경 목록과 충돌합니다."); }
   var prior=local.files.ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
   foreach(var pair in inv) {
    Item was;
    if((!prior.TryGetValue(pair.Key,out was)||was.sha256!=pair.Value.sha256||was.size!=pair.Value.size)&&!seen.Contains(pair.Key))throw new Exception("필요한 파일이 변경 묶음에 누락됐습니다: "+pair.Key);
   }
   foreach(var n in prior.Keys)if(!inv.ContainsKey(n)&&!seen.Contains(n))throw new Exception("삭제 파일이 목록에 누락됐습니다: "+n);
  }
  // 이 설치 폴더의 게임이 실행 중인지. 경로를 읽지 못하면 안전하게 실행 중으로 본다.
  public static bool Running(string root) {
   string exe=Path.Combine(Path.GetFullPath(root),GameExe);
   foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe))) { try { if(Path.GetFullPath(p.MainModule.FileName).Equals(exe,StringComparison.OrdinalIgnoreCase)) return true; } catch { return true; } finally { p.Dispose(); } }
   return false;
  }
  public static void Atomic(string target,string source) {
   Directory.CreateDirectory(Path.GetDirectoryName(target));string tmp=target+".skrupdate-tmp";
   if(File.Exists(tmp)) File.Delete(tmp);
   File.Copy(source,tmp,false);
   if(File.Exists(target)) File.Replace(tmp,target,null); else File.Move(tmp,target);
  }
  static void SaveJson(string path,object value) {
   Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".writing";
   File.WriteAllText(tmp,Json(value),new UTF8Encoding(false));
   if(File.Exists(path)) File.Replace(tmp,path,null);else File.Move(tmp,path);
  }
  // 본편(이용자가 준비한 원본 게임)이 있는지 로컬 파일로만 확인한다. 통신하지 않는다.
  public static void CheckBase(string root) {
   if(!File.Exists(Path.Combine(root,GameExe)))throw new Exception("게임 실행 파일("+GameExe+")이 없습니다.\n한글패치를 본편 설치 폴더에 덮어 풀어 주세요.");
   string requirement=Path.Combine(root,"updates","base-requirements.json");
   if(!File.Exists(requirement))throw new Exception("본편 확인 정보가 없습니다. 한글패치를 다시 풀어 주세요.");
   var info=Parse<BaseInfo>(File.ReadAllText(requirement,Encoding.UTF8));
   if(info.schema!=1||info.app_id!=AppId||info.files==null||info.files.Count==0)throw new Exception("본편 확인 정보 형식 오류입니다.");
   string signature=Hash(requirement),cachePath=Path.Combine(root,"updates","base-check.json");BaseCache cache=null;
   try{cache=Parse<BaseCache>(File.ReadAllText(cachePath,Encoding.UTF8));}catch{}
   var fresh=new BaseCache{requirements=signature,files=new Dictionary<string,Stamp>(StringComparer.OrdinalIgnoreCase)};
   foreach(var f in info.files) {
    string path=Safe(root,f.path,false,true);
    if(!File.Exists(path))throw new Exception("이 파일은 본편이 포함되지 않은 한글패치입니다.\n이용자가 준비한 "+Title+" 본편 설치 폴더(SAKURA.EXE가 있는 폴더)에 패치를 덮어 풀어 주세요.\n없는 파일: "+f.path);
    var stat=new FileInfo(path);Stamp old=null;
    bool cached=cache!=null&&cache.requirements==signature&&cache.files!=null&&cache.files.TryGetValue(f.path,out old)&&old.size==stat.Length&&old.ticks==stat.LastWriteTimeUtc.Ticks;
    if(stat.Length!=f.size||(!cached&&Hash(path)!=f.sha256))throw new Exception("본편 파일이 이 패치의 기준과 다르거나 손상됐습니다.\n지원하는 판본(DVD-ROM판)의 본편인지 확인해 주세요.\n확인할 파일: "+f.path);
    fresh.files[f.path]=new Stamp{size=stat.Length,ticks=stat.LastWriteTimeUtc.Ticks};
   }
   SaveJson(cachePath,fresh);
  }
  static string Work(string root) {
   string p=Path.Combine(root,WorkName);
   if(Directory.Exists(p) && Directory.EnumerateFileSystemEntries(p,"*",SearchOption.AllDirectories).Concat(new[]{p}).Any(x=>(File.GetAttributes(x)&FileAttributes.ReparsePoint)!=0)) throw new Exception("업데이트 임시 폴더에 연결 경로가 있습니다.");
   return p;
  }
  public static void Recover(string root) {
   string work=Work(root),jp=Path.Combine(work,"transaction.json");
   if(!File.Exists(jp)) return;
   if(Running(root)) throw new Exception("미완료 업데이트 복구를 위해 게임을 먼저 종료해 주세요.");
   var j=Parse<Journal>(File.ReadAllText(jp));
   if(!j.committed) foreach(var c in j.changes) {
    string dest=Safe(root,c.path,c.path=="updates/installed.json");
    if(c.existed) { string prior=Path.Combine(work,"prior",c.path); if(!File.Exists(prior)) throw new Exception("복구 파일이 없습니다. "+WorkName+" 폴더를 보존해 주세요.");Atomic(dest,prior); }
    else if(File.Exists(dest)) File.Delete(dest);
   }
   Directory.Delete(work,true);
  }
  public static void Download(string url,string dest,long maxBytes,Action<string> progress) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   var req=(HttpWebRequest)WebRequest.Create(url);req.UserAgent="Sakura-Korean-Patch-Updater/1";req.Timeout=15000;req.ReadWriteTimeout=45000;
   using(var response=(HttpWebResponse)req.GetResponse()) {
    if(response.ResponseUri.Scheme!="https") throw new Exception("안전하지 않은 다운로드 주소입니다.");
    if(response.ContentLength>maxBytes) throw new Exception("다운로드 크기가 안내와 다릅니다.");
    using(var input=response.GetResponseStream()) using(var output=new FileStream(dest,FileMode.Create,FileAccess.Write,FileShare.None)) {
     byte[] buf=new byte[131072];long total=0;int n;var tick=Stopwatch.StartNew();
     while((n=input.Read(buf,0,buf.Length))>0) {total+=n;if(total>maxBytes)throw new Exception("다운로드 크기 초과");output.Write(buf,0,n);if(tick.ElapsedMilliseconds>250){progress("다운로드 중: "+(total/1048576)+" MB");tick.Restart();}}
     output.Flush(true);
    }
   }
  }
  public static Manifest Fetch(Settings s) {
   if(s==null||s.app_id!=AppId||s.repository==null||!Regex.IsMatch(s.repository,"^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$")) throw new Exception("업데이트 저장소 설정이 없습니다.");
   string tmp=Path.GetTempFileName();
   try {Download("https://github.com/"+s.repository+"/releases/latest/download/update-manifest.json",tmp,16*1024*1024,delegate{});return Parse<Manifest>(File.ReadAllText(tmp,Encoding.UTF8));}finally{File.Delete(tmp);}
  }
  public static void Apply(string root,Manifest m,State local,Action<Package,string> fetch,Action<string> progress,int failAfter=-1) {
   Validate(m,local,root);
   if(Ver(m.version)<=Ver(local.version)) return;
   if(Running(root)) throw new Exception("게임을 종료한 뒤 업데이트를 실행해 주세요.");
   string work=Work(root);Recover(root);if(Directory.Exists(work))Directory.Delete(work,true);Directory.CreateDirectory(work);
   var old=local.files.ToDictionary(f=>f.path,f=>f,StringComparer.OrdinalIgnoreCase);
   var originalBase=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
   string basePath=Path.Combine(root,"updates","base-requirements.json");
   if(File.Exists(basePath))originalBase=Parse<BaseInfo>(File.ReadAllText(basePath,Encoding.UTF8)).files.ToDictionary(f=>f.path,f=>f,StringComparer.OrdinalIgnoreCase);
   var changed=new List<Item>();var removals=new List<string>();long priorBytes=0;
   foreach(var f in m.files) {
    string dest=Safe(root,f.path);bool exists=File.Exists(dest);string current=exists?Hash(dest):null;
    if(current==f.sha256)continue;
    Item previous;
    if(exists) {
     bool known=old.TryGetValue(f.path,out previous);
     if(!known)known=originalBase.TryGetValue(f.path,out previous);
     if(!known||previous.sha256!=current)throw new Exception("개별 수정 파일이 있어 덮어쓰지 않았습니다: "+f.path);
     priorBytes+=new FileInfo(dest).Length;
    }
    changed.Add(f);
   }
   foreach(var n in m.deleted) {string dest=Safe(root,n);Item previous;if(File.Exists(dest)){if(!old.TryGetValue(n,out previous))continue;if(Hash(dest)!=previous.sha256)throw new Exception("개별 수정 파일을 삭제하지 않았습니다: "+n);removals.Add(n);priorBytes+=new FileInfo(dest).Length;}}
   // 필요한 공간: 받은 묶음 + 풀어 둔 새 파일 + 복구용 이전본 + 교체할 때 쓰는 임시 사본(가장 큰 파일 하나) + 여유
   long needed=m.packages.Where(p=>changed.Any(f=>f.package==p.name)).Sum(p=>p.size)+changed.Sum(f=>f.size)+priorBytes+(changed.Count>0?changed.Max(f=>f.size):0)+32L*1024*1024;
   if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<needed)throw new Exception("업데이트에 필요한 임시 공간이 부족합니다. (필요: 약 "+(needed/1048576+1)+" MB)");
   var staged=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in m.packages.Where(p=>changed.Any(f=>f.package==p.name))) {
    progress("묶음 받는 중: "+p.name);string zip=Path.Combine(work,p.name);fetch(p,zip);
    if(new FileInfo(zip).Length!=p.size||Hash(zip)!=p.sha256)throw new Exception("다운로드 검사에 실패했습니다: "+p.name);
    var expected=m.files.Where(f=>f.package==p.name).ToDictionary(f=>f.path,StringComparer.OrdinalIgnoreCase);
    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using(var archive=ZipFile.OpenRead(zip)) foreach(var e in archive.Entries) {
     Safe(root,e.FullName);Item f;
     if(!seen.Add(e.FullName)||!expected.TryGetValue(e.FullName,out f)||e.Length!=f.size)throw new Exception("압축 파일 목록이 안내와 다릅니다.");
     if(!changed.Any(c=>c.path==f.path))continue;
     string dest=Path.Combine(work,"new",f.path);Directory.CreateDirectory(Path.GetDirectoryName(dest));e.ExtractToFile(dest);
     if(Hash(dest)!=f.sha256)throw new Exception("파일 검사 실패: "+f.path);staged.Add(f.path,dest);
    }
    if(seen.Count!=expected.Count)throw new Exception("압축 파일에 누락된 항목이 있습니다.");File.Delete(zip);
   }
   if(staged.Count!=changed.Count)throw new Exception("필요한 변경 파일이 모두 도착하지 않았습니다.");
   var next=new State{schema=1,app_id=AppId,version=m.version,base_version=m.base_version,files=m.inventory.Select(f=>new Item{path=f.path,size=f.size,sha256=f.sha256}).ToList()};
   string newState=Path.Combine(work,"state.json");File.WriteAllText(newState,Json(next),new UTF8Encoding(false));staged.Add("updates/installed.json",newState);
   var journal=new Journal{committed=false,changes=new List<Change>()};
   foreach(string n in staged.Keys.Concat(removals)) {
    string dest=Safe(root,n,n=="updates/installed.json");bool exists=File.Exists(dest);
    if(exists){string backup=Path.Combine(work,"prior",n);Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(dest,backup);}
    journal.changes.Add(new Change{path=n,existed=exists});
   }
   string jp=Path.Combine(work,"transaction.json");SaveJson(jp,journal);
   try {
    if(Running(root))throw new Exception("게임이 실행돼 교체를 중단했습니다.");int done=0;
    foreach(var pair in staged.Where(p=>p.Key!="updates/installed.json")) {progress("적용 중: "+pair.Key);Atomic(Safe(root,pair.Key),pair.Value);if(++done==failAfter)throw new Exception("모의 교체 중단");}
    foreach(string n in removals)File.Delete(Safe(root,n));
    Atomic(Safe(root,"updates/installed.json",true),newState);
    journal.committed=true;SaveJson(jp,journal);Directory.Delete(work,true);
   } catch {Recover(root);throw;}
  }
 }
 public static class App {
  const string UpdateTitle="한글패치 업데이트";
  public static Manifest CheckRemote(Func<Manifest> check,bool manual,Action launch,Action<Exception> notify) {
   try{return check();}
   catch(Exception error){notify(error);if(!manual)launch();return null;}
  }
  static void Note(string root,string message) {try{Directory.CreateDirectory(Path.Combine(root,"updates"));File.WriteAllText(Path.Combine(root,"updates","last-check.txt"),DateTime.Now+" "+message,Encoding.UTF8);}catch{}}
  static void Launch(string root) {
   if(Engine.Running(root))return;
   // 게임은 시작할 때 세이브 폴더에 쓸 수 있는지 확인한다. 폴더만 통째로 옮긴 설치본을 위해 없으면 만든다.
   try{Directory.CreateDirectory(Path.Combine(root,"AdvData","SaveData"));}catch{}
   Process.Start(new ProcessStartInfo(Path.Combine(root,Engine.GameExe)){WorkingDirectory=root,UseShellExecute=true});
  }
  public static void Run(string root,bool manual) {
   Application.EnableVisualStyles();bool fresh;
   using(var mutex=new Mutex(true,"Local\\SakuraUpdate-"+Engine.TextHash(Path.GetFullPath(root).ToLowerInvariant()).Substring(0,24),out fresh)) {
    if(!fresh){MessageBox.Show("다른 실행기나 업데이트가 진행 중입니다.",Engine.Title);return;}
    try {
     Engine.Recover(root);
     Engine.CheckBase(root);
     bool network=true;try{network=System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();}catch{}
     if(!manual&&!network){Note(root,"네트워크 없음: 현재 버전 실행");Launch(root);return;}
     var local=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates","installed.json"),Encoding.UTF8));
     var settings=Engine.Parse<Settings>(File.ReadAllText(Path.Combine(root,"updates","settings.json"),Encoding.UTF8));
     Manifest m=null;
     m=CheckRemote(()=>{var found=Engine.Fetch(settings);Engine.Validate(found,local,root);Note(root,"업데이트 확인 완료: "+found.version);return found;},manual,()=>Launch(root),e=>{Note(root,"확인 실패: "+e.Message);if(manual)MessageBox.Show("업데이트를 확인하지 못했습니다.\n"+e.Message,UpdateTitle,MessageBoxButtons.OK,MessageBoxIcon.Information);});
     if(m==null)return;
     if(Engine.Ver(m.version)<=Engine.Ver(local.version)){if(manual)MessageBox.Show("현재 최신 버전입니다. ("+local.version+")",UpdateTitle);else Launch(root);return;}
     string notes=m.notes??"";if(notes.Length>1200)notes=notes.Substring(0,1200)+"…";
     var answer=MessageBox.Show("한글패치 새 버전이 있습니다.\n"+local.version+" → "+m.version+"\n\n"+notes+"\n\n지금 업데이트하시겠습니까?\n세이브는 유지됩니다.",UpdateTitle,MessageBoxButtons.YesNo,MessageBoxIcon.Information);
     if(answer!=DialogResult.Yes){if(!manual)Launch(root);return;}
     if(Engine.Running(root)){MessageBox.Show("게임을 완전히 종료한 뒤 수동 업데이트를 실행해 주세요.",UpdateTitle);return;}
     Exception failure=null;
     using(var form=new Form{Text=UpdateTitle,Width=560,Height=150,StartPosition=FormStartPosition.CenterScreen,ControlBox=false,FormBorderStyle=FormBorderStyle.FixedDialog}) {
      var label=new Label{Left=20,Top=20,Width=510,Height=45,Text="업데이트 준비 중…"};form.Controls.Add(label);
      form.Controls.Add(new ProgressBar{Left=20,Top=75,Width=505,Style=ProgressBarStyle.Marquee});
      Action<string> progress=s=>{if(form.IsHandleCreated)form.BeginInvoke(new Action(()=>label.Text=s));};
      form.Shown+=async delegate {await Task.Run(()=>{try{Engine.Apply(root,m,local,(p,d)=>Engine.Download("https://github.com/"+settings.repository+"/releases/download/"+m.release_tag+"/"+p.name,d,p.size,progress),progress);}catch(Exception e){failure=e;}});form.Close();};
      form.ShowDialog();
     }
     if(failure!=null){Note(root,"업데이트 실패: "+failure.Message);MessageBox.Show("업데이트를 완료하지 못했습니다.\n"+failure.Message,UpdateTitle,MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
     Note(root,"업데이트 적용 완료: "+m.version);if(manual)MessageBox.Show("업데이트가 완료되었습니다. ("+m.version+")",UpdateTitle);else Launch(root);
    } catch(Exception e){MessageBox.Show("실행을 진행할 수 없습니다.\n"+e.Message,Engine.Title,MessageBoxButtons.OK,MessageBoxIcon.Error);}
   }
  }
 }
}
