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

namespace GameFrameX.Online.Storage;

/// <summary>
/// 玩家云存储限制选项（vault:C3 S2.6/VC-2.11：单值上限、单集合键数上限、分页上限）。
/// <para>
/// 维护约束：超限写入一律拒绝（4xxx 参数段），不放行部分写入；标识格式（集合名/键）统一为
/// <c>[A-Za-z0-9_-]{1,64}</c>；调整上限属于装配配置，不改变存储契约。
/// </para>
/// </summary>
public sealed class OnlinePlayerStorageOptions
{
    /// <summary>集合名与键的长度上限。</summary>
    public const int MaxIdentifierLength = 64;

    /// <summary>
    /// 获取或设置单值负载上限（字节；默认 64KB）。
    /// </summary>
    public int MaxPayloadBytes
    {
        get;
        set;
    } = 64 * 1024;

    /// <summary>
    /// 获取或设置单集合活跃键数上限（默认 256；软删键不占额度）。
    /// </summary>
    public int MaxKeysPerCollection
    {
        get;
        set;
    } = 256;

    /// <summary>
    /// 获取或设置单页条数上限（默认 100；超出钳制到上限）。
    /// </summary>
    public int MaxPageSize
    {
        get;
        set;
    } = 100;

    /// <summary>
    /// 校验集合名/键的格式与长度。
    /// </summary>
    /// <param name="identifier">待校验标识。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Length > MaxIdentifierLength)
        {
            return false;
        }

        foreach (var ch in identifier)
        {
            var isLegal = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
            if (!isLegal)
            {
                return false;
            }
        }

        return true;
    }
}
