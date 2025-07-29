using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnifierTSL.Module
{
    // 欸我去，泛型约束不可以是interface，byd微软 
    // class 欸我去
    // 爆！哈基软
    // 顺便帮我创个cs文件吧（我这边创建没有模板，还得调属性
    // OK力
    // IPluginManager.cs
    // 好好好 看看聊天框（
    // 思考，插件目录下的结构怎么安排呢
    public class AssemblyModuleLoader<TModule> where TModule : IModule
    {

    }
}
