using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using SakuraUpdate;
// 합성 파일만 쓰는 오프라인 시험. 실제 GitHub 연결이나 게임 실행은 하지 않는다.
class UpdaterTests {
 static int passed=0;
 static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("통과: "+name);}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch{failed=true;}Check(failed,name);}
 static string Write(string root,string path,string data){string p=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,data,new UTF8Encoding(false));return p;}
 static Item Info(string file,string path,string package=null){return new Item{path=path,size=new FileInfo(file).Length,sha256=Engine.Hash(file),package=package};}
 static void Main(){string root=Path.Combine(Path.GetTempPath(),"사쿠라 업데이트 시험 "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try {
   const string MES="AdvData/MES/1000.mes",SAVE="AdvData/SaveData/sakura00.dat",PKG="sakura-update-1.1.0-001.zip";
   Write(root,"SAKURA.EXE","게임 실행 파일 자리");
   string a=Write(root,MES,"이전 번역"),keep=Write(root,SAVE,"사용자 세이브");
   var old=new State{schema=1,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{Info(a,MES)}};
   Write(root,"updates/installed.json",Engine.Json(old));
   string zip=Path.Combine(root,"fixture.zip"),stage=Write(root,"fixture/new.txt","수정 번역");
   using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))z.CreateEntryFromFile(stage,MES);
   var item=Info(stage,MES,PKG);
   var m=new Manifest{schema=1,app_id=Engine.AppId,version="1.1.0",base_version="1.0.0",release_tag="v1.1.0",notes="시험",files=new List<Item>{item},inventory=new List<Item>{item},deleted=new List<string>(),packages=new List<Package>{new Package{name=PKG,size=new FileInfo(zip).Length,sha256=Engine.Hash(zip)}}};
   Action<Package,string> fetch=(p,d)=>File.Copy(zip,d);Action<string> quiet=s=>{};
   var original=File.ReadAllBytes(a);var saved=Engine.Hash(keep);
   Reject(()=>Engine.Apply(root,m,old,fetch,quiet,1),"교체 도중 오류 감지");
   Check(File.ReadAllBytes(a).SequenceEqual(original)&&Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates/installed.json"))).version=="1.0.0","실패 후 본문과 버전 복구");
   Check(!Directory.Exists(Path.Combine(root,".sakura-update")),"복구 뒤 임시 폴더 정리");
   Engine.Apply(root,m,old,fetch,quiet);Check(Engine.Hash(a)==item.sha256,"한글·공백 경로 업데이트 성공");Check(Engine.Hash(keep)==saved,"사용자 세이브 보존");
   Check(!Directory.Exists(Path.Combine(root,".sakura-update")),"성공 뒤 임시 폴더 정리");
   var now=Engine.Parse<State>(File.ReadAllText(Path.Combine(root,"updates/installed.json")));int calls=0;
   Check(now.version=="1.1.0"&&now.files.Count==1&&now.files[0].sha256==item.sha256,"설치 기록 갱신");
   Engine.Apply(root,m,now,(p,d)=>{calls++;},quiet);Check(calls==0,"같은 버전 중복 다운로드 없음");
   foreach(string path in new[]{"../escape.dll","AdvData/MES/../../escape.mes",SAVE,"AdvData/SaveData/system.dat","AdvData/MES/1000.mes:stream","AdvData/MES/CON.mes","/absolute.mes","AdvData/MES/1000.mes.","AdvData/GRP/SYSTEM/../../MES/x.mes","updates/settings.json","SAKURA_한글판.exe","한글패치_업데이트.exe","AdvData/VOICE.DAT","AdvData/MUSIC/SAKURA_01.pcm","AdvData/DAT/names.dat","AdvData/MOVIE/0.mpeg","uinst.exe","AdvData/GRP/other/x.CRX","AdvData\\MES\\1000.mes","C:/Windows/x.mes"})Reject(()=>Engine.Safe(root,path),"보호 경로 차단 "+path);
   foreach(string path in new[]{"SAKURA.EXE","SkrKR034.ttf","SkrKR034_NOTICE.txt","사용설명서.txt","AdvData/MES/music.mes","AdvData/GRP/SYSTEM/title_back2.CRX","AdvData/GRP/BG/bg001a.CRX","AdvData/GRP/bst/BT1001.CRX","AdvData/GRP/battle/c01.CRX","updates/SakuraUpdater.dll","updates/base-requirements.json"}){Engine.Safe(root,path);Check(true,"관리 경로 허용 "+path);}
   string oldPath=item.path;item.path=SAVE;Reject(()=>Engine.Validate(m,old,root),"원격 목록의 세이브 변경 차단");item.path=oldPath;
   m.files.Add(item);Reject(()=>Engine.Validate(m,old,root),"중복 파일 경로 차단");m.files.RemoveAt(1);
   m.base_version="2.0.0";Reject(()=>Engine.Validate(m,old,root),"다른 배포 기준 버전 차단");m.base_version="1.0.0";
   m.app_id="canvas3-ko";Reject(()=>Engine.Validate(m,old,root),"다른 게임의 업데이트 정보 차단");m.app_id=Engine.AppId;
   m.release_tag="v9.9.9";Reject(()=>Engine.Validate(m,old,root),"태그와 버전 불일치 차단");m.release_tag="v1.1.0";
   m.packages[0].name="canvas3-update-1.1.0-001.zip";Reject(()=>Engine.Validate(m,old,root),"다른 이름 규칙의 묶음 차단");m.packages[0].name=PKG;
   foreach(string bad in new[]{"1.0","1.0.0-beta","v1.0.0","1.0.0.0"})Reject(()=>Engine.Ver(bad),"버전 형식 차단 "+bad);
   Check(Engine.Ver("1.0.10")>Engine.Ver("1.0.9"),"버전은 숫자로 비교");
   File.WriteAllBytes(a,original);Write(root,"updates/installed.json",Engine.Json(old));
   string hash=m.packages[0].sha256;m.packages[0].sha256=new string('0',64);Reject(()=>Engine.Apply(root,m,old,fetch,quiet),"손상 다운로드 해시 차단");Check(File.ReadAllBytes(a).SequenceEqual(original),"손상 응답은 게임 미변경");m.packages[0].sha256=hash;
   // 묶음 안에 목록에 없는 파일이 들어 있으면 거부한다.
   string evil=Path.Combine(root,"evil.zip");using(var z=ZipFile.Open(evil,ZipArchiveMode.Create)){z.CreateEntryFromFile(stage,MES);z.CreateEntryFromFile(stage,"AdvData/MES/9999.mes");}
   var evilPkg=new Package{name=PKG,size=new FileInfo(evil).Length,sha256=Engine.Hash(evil)};var keepPkg=m.packages[0];m.packages[0]=evilPkg;
   Reject(()=>Engine.Apply(root,m,old,(p,d)=>File.Copy(evil,d),quiet),"묶음 안의 목록 외 파일 차단");Check(File.ReadAllBytes(a).SequenceEqual(original)&&!File.Exists(Path.Combine(root,"AdvData/MES/9999.mes")),"목록 외 파일은 설치되지 않음");m.packages[0]=keepPkg;
   File.WriteAllText(a,"사용자 개별 수정");Reject(()=>Engine.Apply(root,m,old,fetch,quiet),"설치 후 개별 수정 보존");Check(File.ReadAllText(a)=="사용자 개별 수정","개별 수정 파일 내용 유지");File.WriteAllBytes(a,original);
   // 강제 종료를 모사: 저널과 복구본은 있고 대상만 교체된 상태.
   Write(root,".sakura-update/prior/"+MES,"이전 번역");Write(root,".sakura-update/transaction.json",Engine.Json(new Journal{committed=false,changes=new List<Change>{new Change{path=MES,existed=true}}}));
   File.WriteAllText(a,"중간 교체");Engine.Recover(root);Check(File.ReadAllBytes(a).SequenceEqual(original),"재실행 시 중단된 교체 복구");
   // 누적 업데이트: 1.0.0 에서 1.1.0 을 건너뛰고 1.2.0 으로. 새 파일 추가와 관리 파일 삭제 포함.
   string img=Write(root,"fixture/img.bin","새 그림"),zip2=Path.Combine(root,"fixture2.zip");const string PKG2="sakura-update-1.2.0-001.zip",CRX="AdvData/GRP/SYSTEM/title_back2.CRX",GONE="AdvData/MES/9000.mes";
   string gone=Write(root,GONE,"없어질 대본");
   var old2=new State{schema=1,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{Info(a,MES),Info(gone,GONE)}};
   Write(root,"updates/installed.json",Engine.Json(old2));
   using(var z=ZipFile.Open(zip2,ZipArchiveMode.Create)){z.CreateEntryFromFile(stage,MES);z.CreateEntryFromFile(img,CRX);}
   var items2=new List<Item>{Info(stage,MES,PKG2),Info(img,CRX,PKG2)};
   var m2=new Manifest{schema=1,app_id=Engine.AppId,version="1.2.0",base_version="1.0.0",release_tag="v1.2.0",notes="누적",files=items2,inventory=items2,deleted=new List<string>{GONE},packages=new List<Package>{new Package{name=PKG2,size=new FileInfo(zip2).Length,sha256=Engine.Hash(zip2)}}};
   Engine.Apply(root,m2,old2,(p,d)=>File.Copy(zip2,d),quiet);
   Check(Engine.Hash(a)==items2[0].sha256&&Engine.Hash(Path.Combine(root,CRX))==items2[1].sha256&&!File.Exists(gone),"중간 버전을 건너뛴 누적 업데이트(추가·교체·삭제)");
   Check(Engine.Hash(keep)==saved,"누적 업데이트 뒤에도 세이브 보존");
   // 삭제 대상이 사용자 수정본이면 지우지 않는다.
   File.WriteAllBytes(a,original);if(File.Exists(Path.Combine(root,CRX)))File.Delete(Path.Combine(root,CRX));File.WriteAllText(gone,"사용자가 고친 대본");Write(root,"updates/installed.json",Engine.Json(old2));
   Reject(()=>Engine.Apply(root,m2,old2,(p,d)=>File.Copy(zip2,d),quiet),"사용자 수정 파일은 삭제하지 않음");Check(File.Exists(gone)&&File.ReadAllBytes(a).SequenceEqual(original),"삭제 거부 시 다른 파일도 미변경");
   File.Delete(gone);Write(root,"updates/installed.json",Engine.Json(old));
   // 0개 변경/삭제 없는 초기 릴리스도 정상 정보이다.
   m.files.Clear();m.packages.Clear();Reject(()=>Engine.Validate(m,old,root),"변경 파일 누락을 버전 완료로 처리하지 않음");
   m.inventory=old.files;Engine.Validate(m,old,root);Check(true,"빈 변경 목록 허용");
   m.version="1.2.0";m.release_tag="v1.2.0";
   Reject(()=>Engine.Validate(m,now,root),"예전 상태로 되돌리는 업데이트에도 실제 파일 요구");
   foreach(var status in new[]{WebExceptionStatus.ConnectFailure,WebExceptionStatus.NameResolutionFailure,WebExceptionStatus.Timeout,WebExceptionStatus.ProtocolError}) {
    int launch=0,notice=0;var answer=App.CheckRemote(()=>{throw new WebException("연결 실패 모의",status);},false,()=>launch++,e=>notice++);
    Check(answer==null&&launch==1&&notice==1,"연결 실패 시 현재 게임 실행 경로: "+status);
   }
   int manualLaunch=0;App.CheckRemote(()=>{throw new WebException("오프라인");},true,()=>manualLaunch++,e=>{});Check(manualLaunch==0,"수동 업데이트 실패는 게임을 임의 실행하지 않음");
   // 본편 확인
   const string BASE="AdvData/DAT/names.dat";
   var baseFile=new Item{path=BASE,size=5,sha256=Engine.TextHash("dummy").ToLowerInvariant()};
   Write(root,"updates/base-requirements.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile}}));
   Reject(()=>Engine.CheckBase(root),"본편 파일 없는 폴더 감지");
   Write(root,BASE,"dummy");Engine.CheckBase(root);Check(true,"본편 로컬 파일 확인 — 외부 통신 없음");
   Engine.CheckBase(root);Check(File.Exists(Path.Combine(root,"updates/base-check.json")),"본편 확인 캐시 재사용");
   Reject(()=>Engine.Safe(root,BASE),"자동 업데이트로 본편 원본 배포 금지");
   Write(root,BASE,"different");Reject(()=>Engine.CheckBase(root),"본편 파일 변경 감지");
   Write(root,BASE,"dummy");File.Delete(Path.Combine(root,"SAKURA.EXE"));Reject(()=>Engine.CheckBase(root),"게임 실행 파일 없는 폴더 감지");Write(root,"SAKURA.EXE","게임 실행 파일 자리");
   // 본편 확인용이던 원본 파일(예: 시스템 대본)에 나중에 패치가 추가되는 경우도 사용자 수정과 구별한다.
   const string SYS="AdvData/MES/main.mes",PKG3="sakura-update-1.0.1-001.zip";
   string core=Write(root,SYS,"original-system-script");var coreOld=Info(core,SYS);
   string req=Write(root,"updates/base-requirements.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile,coreOld}}));
   var coreState=new State{schema=1,app_id=Engine.AppId,version="1.0.0",base_version="1.0.0",files=new List<Item>{Info(req,"updates/base-requirements.json")}};
   Write(root,"updates/installed.json",Engine.Json(coreState));
   string coreNew=Write(root,"fixture/main.mes","patched-system-script"),reqNew=Write(root,"fixture/req.json",Engine.Json(new BaseInfo{schema=1,app_id=Engine.AppId,files=new List<Item>{baseFile}}));
   string coreZip=Path.Combine(root,"core.zip");
   using(var z=ZipFile.Open(coreZip,ZipArchiveMode.Create)){z.CreateEntryFromFile(coreNew,SYS);z.CreateEntryFromFile(reqNew,"updates/base-requirements.json");}
   var coreItems=new List<Item>{Info(coreNew,SYS,PKG3),Info(reqNew,"updates/base-requirements.json",PKG3)};
   var coreManifest=new Manifest{schema=1,app_id=Engine.AppId,version="1.0.1",base_version="1.0.0",release_tag="v1.0.1",files=coreItems,inventory=coreItems,deleted=new List<string>(),packages=new List<Package>{new Package{name=PKG3,size=new FileInfo(coreZip).Length,sha256=Engine.Hash(coreZip)}}};
   Engine.Apply(root,coreManifest,coreState,(p,d)=>File.Copy(coreZip,d),quiet);Engine.CheckBase(root);
   Check(Engine.Hash(core)==Engine.Hash(coreNew),"원본 파일에 후속 패치 추가 및 본편 조건 갱신");
   Check(Engine.Hash(keep)==saved,"모든 시험 뒤 세이브 보존");
   Console.WriteLine("오프라인 시험 "+passed+"개 통과. 실제 GitHub 연결/게임 실행 시험 아님.");
  } finally { if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true); }
 }
}
