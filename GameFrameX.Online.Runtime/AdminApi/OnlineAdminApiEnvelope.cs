// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Http.Normalization;
using GameFrameX.Foundation.Json;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// Online admin API 双层信封构造器（change C122：外层 Foundation <see cref="HttpJsonResultData{T}"/> 信封 code=0，
/// 内层 <see cref="OnlineAdminApiResponse"/> 以 JSON 字符串形态置于信封 Data 字段）。
/// <para>
/// 维护约束：双层形态对齐 Admin 侧 <c>OnlineServerClient</c> 的 <c>ToHttpJsonResult&lt;OnlineApiResponse&lt;T&gt;&gt;</c>
/// 双层解包契约——外层失败（非 0）会被 Admin 判为基础设施异常，因此业务错误只进内层 <c>Code</c>，
/// 外层恒为成功信封；序列化统一走 <see cref="JsonHelper"/>（PascalCase 保真 + 枚举字符串 + 中文不转义）。
/// </para>
/// </summary>
public static class OnlineAdminApiEnvelope
{
    /// <summary>
    /// 将内层业务响应包装为双层信封 JSON 字符串（HTTP 响应体原文）。
    /// </summary>
    /// <param name="response">内层业务响应。</param>
    /// <returns>外层信封 JSON 字符串（Foundation camelCase 形态 <c>{"code":0,"message":"","data":"&lt;内层 JSON&gt;"}</c>）。</returns>
    public static string Write(OnlineAdminApiResponse response)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var innerJson = JsonHelper.Serialize(response);
        return HttpJsonResultData<string>.SuccessString(innerJson);
    }

    /// <summary>
    /// 将已序列化的内层响应 JSON 直接包装为双层信封（幂等 Replay 路径透传首次响应原文）。
    /// </summary>
    /// <param name="innerJson">内层响应 JSON 字符串（首次执行时序列化的原文）。</param>
    /// <returns>外层信封 JSON 字符串。</returns>
    public static string WriteRaw(string innerJson)
    {
        if (string.IsNullOrEmpty(innerJson))
        {
            throw new ArgumentException("Inner response JSON must not be null or empty.", nameof(innerJson));
        }

        return HttpJsonResultData<string>.SuccessString(innerJson);
    }
}
