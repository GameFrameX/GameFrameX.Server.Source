// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Timer;
using GameFrameX.DataBase;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Components;

/// <summary>
/// 数据状态组件
/// </summary>
public sealed class StateComponent
{
    private static readonly ConcurrentBag<Func<bool, bool, Task>> SaveFuncMap = new();

    /// <summary>
    /// 注册回存
    /// </summary>
    /// <param name="shutdown"></param>
    public static void AddShutdownSaveFunc(Func<bool, bool, Task> shutdown)
    {
        SaveFuncMap.Add(shutdown);
    }

    /// <summary>
    /// 当游戏出现异常，导致无法正常回存，才需要将force=true
    /// 由后台http指令调度
    /// </summary>
    /// <param name="force"></param>
    /// <returns></returns>
    public static async Task SaveAllAsync(bool force = false)
    {
        try
        {
            var begin = DateTime.Now;
            var tasks = new List<Task>();
            foreach (var saveFunc in SaveFuncMap)
            {
                tasks.Add(saveFunc(true, force));
            }

            await Task.WhenAll(tasks);
            // Localization: Core.StateComponent.SaveAllStateTimeLog - StateComponent.SaveAllAsync 保存所有状态，耗时: {0}毫秒
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Core.StateComponent.SaveAllStateTimeLog, (DateTime.Now - begin).TotalMilliseconds));
        }
        catch (Exception e)
        {
            // Localization: Core.StateComponent.SaveAllStateErrorLog - StateComponent.SaveAllAsync 保存所有状态错误\n{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.StateComponent.SaveAllStateErrorLog, e));
        }
    }

    /// <summary>
    /// 定时回存所有数据
    /// </summary>
    public static async Task TimerSaveAsync()
    {
        try
        {
            foreach (var func in SaveFuncMap)
            {
                await func(false, false);
                if (!GlobalTimer.IsWorking)
                {
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // Localization: Core.StateComponent.TimerSaveStateErrorLog - StateComponent.TimerSaveAsync 定时保存状态错误，异常:{0}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Core.StateComponent.TimerSaveStateErrorLog, e));
        }
    }
}
