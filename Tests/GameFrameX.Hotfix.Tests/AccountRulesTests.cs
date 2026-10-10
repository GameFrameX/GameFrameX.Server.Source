// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
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

using GameFrameX.Hotfix.Logic.ServerRole.Account;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Account 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖账号名/密码合法性边界、盐生成与密码哈希确定性。
/// </remarks>
public class AccountRulesTests
{
    /// <summary>
    /// 账号名合法性：空白与长度越界拒绝、边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("    ", false)]
    [InlineData("abc", false)]
    [InlineData("abcd", true)]
    [InlineData("abcdefgh234567890123456789012345", true)]
    [InlineData("abcdefgh2345678901234567890123456", false)]
    public void IsNameValid_BoundaryCases(string accountName, bool expected)
    {
        Assert.Equal(expected, AccountRules.IsNameValid(accountName));
    }

    /// <summary>
    /// 账号名达到上限长度（32）仍合法，超限（33）非法。
    /// </summary>
    [Fact]
    public void IsNameValid_LengthLimit()
    {
        Assert.True(AccountRules.IsNameValid(new string('a', AccountRules.MaxNameLength)));
        Assert.False(AccountRules.IsNameValid(new string('a', AccountRules.MaxNameLength + 1)));
        Assert.True(AccountRules.IsNameValid(new string('a', AccountRules.MinNameLength)));
        Assert.False(AccountRules.IsNameValid(new string('a', AccountRules.MinNameLength - 1)));
    }

    /// <summary>
    /// 密码合法性：空与过短拒绝、边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("123456", true)]
    [InlineData("1234567", true)]
    public void IsPasswordValid_BoundaryCases(string password, bool expected)
    {
        Assert.Equal(expected, AccountRules.IsPasswordValid(password));
    }

    /// <summary>
    /// 密码哈希确定性：同盐同密码结果一致；同密码不同盐结果不同；输出为 64 位十六进制小写。
    /// </summary>
    [Fact]
    public void HashPassword_DeterministicAndSalted()
    {
        var hash1 = AccountRules.HashPassword("salt0001", "secret-password");
        var hash2 = AccountRules.HashPassword("salt0001", "secret-password");
        var hash3 = AccountRules.HashPassword("salt0002", "secret-password");
        var hash4 = AccountRules.HashPassword("salt0001", "secret-password2");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
        Assert.NotEqual(hash1, hash4);
        Assert.Equal(64, hash1.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    /// <summary>
    /// 盐生成：32 位十六进制且不重复。
    /// </summary>
    [Fact]
    public void CreateSalt_UniqueHex()
    {
        var salt1 = AccountRules.CreateSalt();
        var salt2 = AccountRules.CreateSalt();

        Assert.NotEqual(salt1, salt2);
        Assert.Matches("^[0-9a-f]{32}$", salt1);
    }
}
