// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file at the root of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Apps.ServerRole.{{Name}}.Component;
using GameFrameX.Apps.ServerRole.{{Name}}.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.{{Name}};

/// <summary>
/// {{Name}} 服务器（{{Name}} Role）业务组件代理。
/// </summary>
/// <remarks>
/// 骨架代理（gfx-role 模板生成）：回显链路样例——规则判定走 <see cref="{{Name}}Rules"/>（纯函数，可单测），
/// 状态编排读写 <see cref="{{Name}}State"/>；真实业务按既有惯例替换/扩充方法。
/// </remarks>
public class {{Name}}ComponentAgent : StateComponentAgent<{{Name}}Component, {{Name}}State>
{
    /// <summary>
    /// 回显：文本合法则递增计数并原样返回，否则回 <see cref="{{Name}}ErrorCode.TextInvalid"/>。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">回显请求。</param>
    /// <param name="response">回显响应。</param>
    public Task OnEchoAsync(long playerId, Req{{Name}}Echo request, Resp{{Name}}Echo response)
    {
        if (!{{Name}}Rules.IsTextValid(request.Text))
        {
            response.ErrorCode = (int){{Name}}ErrorCode.TextInvalid;
            return Task.CompletedTask;
        }

        State.EchoCount += 1;
        response.Text = request.Text;
        return Task.CompletedTask;
    }
}
