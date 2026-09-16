// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Runtime;

/// <summary>
/// LiveOps 登记条目（append-only；载荷原样登记不解释，见 <see cref="OnlineLiveOpsRegistry"/>）。
/// </summary>
public sealed class OnlineLiveOpsEntry
{
    /// <summary>获取登记序号（进程内单调递增）。</summary>
    public long Sequence
    {
        get;
        set;
    }

    /// <summary>获取登记操作。</summary>
    public OnlineLiveOpsOperation Operation
    {
        get;
        set;
    }

    /// <summary>获取配置种类。</summary>
    public string Kind
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取配置键。</summary>
    public string Key
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取版本号（Sync 操作为空串）。</summary>
    public string Version
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取载荷原文。</summary>
    public string PayloadText
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取操作者。</summary>
    public string OperatorId
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取关联标识。</summary>
    public string CorrelationId
    {
        get;
        set;
    } = string.Empty;

    /// <summary>获取登记时刻（UTC 毫秒）。</summary>
    public long CreatedAtTime
    {
        get;
        set;
    }
}
