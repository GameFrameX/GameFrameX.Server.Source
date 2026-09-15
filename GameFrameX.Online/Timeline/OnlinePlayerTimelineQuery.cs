//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 玩家时间线查询条件（作用域与玩家主体位来自 <see cref="Scope.OnlineScope"/>，本类型不重复承载玩家标识）。
/// <para>
/// 维护约束：玩家标识只在作用域里出现一次——对齐 C95 <c>OnlineAssetQueryService</c> 的「资产查询必须绑定玩家主体位」先例。
/// 若查询体再带一份玩家标识，两份不一致时就会出现「按 A 授权、按 B 取数」的越权读取，且这类缺陷在常规用例里不可见。
/// </para>
/// </summary>
public sealed class OnlinePlayerTimelineQuery
{
    /// <summary>
    /// 获取或设置分组过滤（取值见 <see cref="OnlinePlayerTimelineGroup"/> 六值；null 或空 = 全部分组）。
    /// <para>
    /// 未知分组值返回**空列表而非报错**：分组值由消费方枚举给出，未知值通常是跨仓版本错配，
    /// 空列表（与「该分组当前无数据」同构）不会泄露分组是否存在，也不会让管理台整页报错。
    /// </para>
    /// </summary>
    public string Group
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置时间窗起点（Unix 秒，含边界；null = 不限）。
    /// </summary>
    public long? StartTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置时间窗终点（Unix 秒，含边界；null = 不限）。
    /// </summary>
    public long? EndTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分页游标（由服务返回的不透明令牌原样回传；null 或空 = 取首页）。
    /// </summary>
    public string Cursor
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置页大小（有效区间 1～200；小于 1 视为未指定并回落默认值 50；大于 200 返回参数非法）。
    /// </summary>
    public int PageSize
    {
        get;
        set;
    }
}
