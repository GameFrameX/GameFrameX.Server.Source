// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
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
//   官方文档：https://gameframex.doc/alianblank.com/
//   Official Documentation: https://gameframex.doc/alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Localization;

/// <summary>
/// Hotfix 模块本地化消息键。
/// </summary>
public static partial class Keys
{
    /// <summary>
    /// Hotfix 模块消息键。
    /// </summary>
    public static class Hotfix
    {
        /// <summary>
        /// Campaign 参数非法，code={0}
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.MailCampaign.InvalidParameter
        /// 用途: 发布 / 预览 Campaign 前参数校验失败时抛出。
        /// 参数: {0} - 校验错误码（MailCampaignErrorCode）
        /// </remarks>
        public const string MailCampaignInvalidParameter = "Hotfix.MailCampaign.InvalidParameter";

        /// <summary>
        /// Campaign 已发布或已撤回，主体字段不可修改（B1）。CampaignId={0}
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.MailCampaign.PublishedOrRevokedImmutable
        /// 用途: 已发布或已撤回的 Campaign 再次提交发布时抛出（B1 不可逆边界）。
        /// 参数: {0} - CampaignId
        /// </remarks>
        public const string MailCampaignPublishedOrRevokedImmutable = "Hotfix.MailCampaign.PublishedOrRevokedImmutable";

        /// <summary>
        /// 登录消息类型 {0} 缺少 {1}。
        /// </summary>
        /// <remarks>
        /// 键名: Hotfix.Startup.LoginMessageTypeMissingAttribute
        /// 用途: 启动时读取登录消息码，消息类型缺少 MessageTypeHandlerAttribute 特性时抛出。
        /// 参数: {0} - 消息类型全名；{1} - 缺少的特性名
        /// </remarks>
        public const string LoginMessageTypeMissingAttribute = "Hotfix.Startup.LoginMessageTypeMissingAttribute";
    }
}
