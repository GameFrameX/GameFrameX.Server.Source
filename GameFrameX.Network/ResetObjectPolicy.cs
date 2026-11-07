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
using System.Reflection;
using GameFrameX.Network.Abstractions;
using Microsoft.Extensions.ObjectPool;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Network;

/// <summary>
/// Custom pooled object policy that resets objects to default values / 自定义池化对象策略，将对象重置为默认值
/// </summary>
/// <typeparam name="T">The type of objects in the pool / 池中对象的类型</typeparam>
internal class ResetObjectPolicy<T> : IPooledObjectPolicy<T> where T : class, IMessageObject, new()
{
    public T Create()
    {
        return new T();
    }

    public bool Return(T obj)
    {
        if (obj == null)
        {
            return false;
        }

        // Reset object to default values / 将对象重置为默认值
        obj.Clear();
        if (obj is INetworkMessage networkMessage)
        {
            // 身份字段不在各消息自身 Clear() 覆盖范围内：归零 MessageId（发送/接收链路上线缆前都会重设）
            // 并重生成 UniqueId，使池复用与 new T() 身份语义等价，避免复用消息携带上一条消息的 UniqueId 串台
            networkMessage.SetMessageId(0);
            networkMessage.UpdateUniqueId();
        }

        return true;
    }
}
