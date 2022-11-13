using System.Threading.Tasks;
using MvvmCross.ViewModels;
using ProxySuper.Core.Helpers;
using ProxySuper.Core.ViewModels;

namespace ProxySuper.Core
{
    public class App : MvxApplication
    {
        public override void Initialize()
        {
            // 获取版本.
            Task.Run(HttpRequester.InitXrayCoreVersionList);
            RegisterAppStart<HomeViewModel>();
        }
    }
}
