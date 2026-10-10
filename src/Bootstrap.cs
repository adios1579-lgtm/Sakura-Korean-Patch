using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
// SAKURA～설월화～ 한글판 실행기 / 수동 업데이트 실행기.
// 같은 실행 파일을 두 이름으로 배포한다. 파일 이름에 "업데이트"가 들어 있거나 --update 인수가 있으면
// 수동 업데이트 모드(게임을 실행하지 않음), 아니면 업데이트 확인 후 게임을 실행한다.
class Bootstrap {
    [STAThread] static void Main(string[] args) {
        try {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            bool manual=Path.GetFileNameWithoutExtension(Application.ExecutablePath).Contains("업데이트") || Array.IndexOf(args,"--update")>=0;
            // 바이트로 읽어 DLL을 잠그지 않는다. 업데이트 엔진도 자기 자신을 갱신할 수 있다.
            var a=Assembly.Load(File.ReadAllBytes(Path.Combine(root,"updates","SakuraUpdater.dll")));
            a.GetType("SakuraUpdate.App").GetMethod("Run").Invoke(null,new object[]{root,manual});
        } catch(Exception e) {
            MessageBox.Show((e.InnerException??e).Message,"SAKURA～설월화～ 실행 오류",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}
