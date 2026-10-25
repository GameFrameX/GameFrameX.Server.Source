// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX

using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 部署目录发现语义测试（C303 / GFX-1014）：裸 drop dll（identity 不在 deps.json）按磁盘路径落地即被发现；
/// 命中命名约定的损坏产物与类型无法完整加载的能力包 fail-fast 一次报全，不再静默缺席。
/// <para>
/// 探测程序集用独立前缀（<c>CapabilityDeploymentProbe*</c>），与机制族前缀 <c>GameFrameX.Tests</c> 无 glob
/// 交集，避免污染并行运行的机制装配用例。
/// </para>
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityDeploymentDiscoveryTests
{
    /// <summary>
    /// 裸 drop dll（不在 deps.json 的 identity）落地即被发现：按磁盘路径加载并发现其中的能力模块。
    /// </summary>
    [Fact]
    public void LoadDeploymentAssemblies_BareDroppedAssembly_IsDiscovered()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CapabilityDeploymentProbe.Bare.dll");
        EmitSingleModuleAssembly(path, "CapabilityDeploymentProbe.Bare", "CapabilityDeploymentProbe.BareModule", "capability-deployment-probe-bare");
        try
        {
            var assemblies = CapabilityAssembly.LoadDeploymentAssemblies("CapabilityDeploymentProbe");
            var module = Assert.Single(CapabilityAssembly.DiscoverFromDeploymentDirectory("CapabilityDeploymentProbe"));

            Assert.Equal("CapabilityDeploymentProbe.Bare", Assert.Single(assemblies).GetName().Name);
            Assert.Equal("capability-deployment-probe-bare", module.Manifest.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 命中命名约定的损坏产物不再静默跳过：汇总抛 <see cref="CapabilityAssemblyException"/>，问题条目含文件路径。
    /// </summary>
    [Fact]
    public void LoadDeploymentAssemblies_CorruptArtifact_FailsFastWithProblems()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CapabilityDeploymentProbeCorrupt.Broken.dll");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, });
        try
        {
            var exception = Assert.Throws<CapabilityAssemblyException>(() => CapabilityAssembly.LoadDeploymentAssemblies("CapabilityDeploymentProbeCorrupt"));

            Assert.Contains(path, Assert.Single(exception.Problems));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 类型无法完整加载的能力包（依赖缺失）不再静默跳过：带程序集名定位信息 fail-fast。
    /// </summary>
    [Fact]
    public void DiscoverModules_UnloadableTypes_FailsFastWithAssemblyName()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CapabilityDeploymentScanProbe.Broken.dll");
        EmitAssemblyWithUnresolvableReference(path, "CapabilityDeploymentScanProbe.Broken");
        try
        {
            var assembly = Assembly.LoadFrom(path);

            var exception = Assert.Throws<InvalidOperationException>(() => CapabilityAssembly.DiscoverModules(new[] { assembly, }));
            Assert.Contains("CapabilityDeploymentScanProbe.Broken", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 发射一个实现 <see cref="ICapabilityModule"/> 的最小能力模块程序集并保存到指定路径（identity 不进 deps.json，
    /// 构造"裸 drop dll"部署形态）。
    /// </summary>
    /// <param name="path">保存路径。</param>
    /// <param name="assemblyIdentityName">程序集 identity 名。</param>
    /// <param name="moduleTypeName">模块实现类型全名。</param>
    /// <param name="manifestId">manifest 能力 Id。</param>
    private static void EmitSingleModuleAssembly(string path, string assemblyIdentityName, string moduleTypeName, string manifestId)
    {
        var assemblyName = new AssemblyName(assemblyIdentityName);
        var assemblyBuilder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyIdentityName);
        var typeBuilder = moduleBuilder.DefineType(moduleTypeName, TypeAttributes.Public | TypeAttributes.Class, typeof(object), new[] { typeof(ICapabilityModule), });
        var manifestField = typeBuilder.DefineField("manifest", typeof(CapabilityManifest), FieldAttributes.Private);

        var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var constructorGenerator = constructorBuilder.GetILGenerator();
        // Object 的构造器在 .NET 10 反射面不可取（GetConstructor 返回 null）；Reflection.Emit 生成的类型不做 IL 校验，直接省略基类构造调用。
        constructorGenerator.Emit(OpCodes.Ldarg_0);
        constructorGenerator.Emit(OpCodes.Ldstr, manifestId);
        constructorGenerator.Emit(OpCodes.Ldstr, "1.0.0");
        constructorGenerator.Emit(OpCodes.Ldnull);
        constructorGenerator.Emit(OpCodes.Ldnull);
        constructorGenerator.Emit(OpCodes.Ldnull);
        constructorGenerator.Emit(OpCodes.Ldnull);
        constructorGenerator.Emit(OpCodes.Newobj, typeof(CapabilityManifest).GetConstructor(new[] { typeof(string), typeof(string), typeof(System.Collections.Generic.IReadOnlyList<string>), typeof(System.Collections.Generic.IReadOnlyList<string>), typeof(System.Collections.Generic.IReadOnlyList<CapabilityOptionalDependency>), typeof(Type), }));
        constructorGenerator.Emit(OpCodes.Stfld, manifestField);
        constructorGenerator.Emit(OpCodes.Ret);

        var manifestProperty = typeBuilder.DefineProperty("Manifest", PropertyAttributes.None, typeof(CapabilityManifest), null);
        var manifestGetter = typeBuilder.DefineMethod("get_Manifest", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Virtual, typeof(CapabilityManifest), Type.EmptyTypes);
        var manifestGetterGenerator = manifestGetter.GetILGenerator();
        manifestGetterGenerator.Emit(OpCodes.Ldarg_0);
        manifestGetterGenerator.Emit(OpCodes.Ldfld, manifestField);
        manifestGetterGenerator.Emit(OpCodes.Ret);
        manifestProperty.SetGetMethod(manifestGetter);
        typeBuilder.DefineMethodOverride(manifestGetter, typeof(ICapabilityModule).GetProperty("Manifest").GetGetMethod());

        var registerMethod = typeBuilder.DefineMethod("Register", MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.Virtual, typeof(void), new[] { typeof(CapabilityRegistrationContext), });
        registerMethod.GetILGenerator().Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(registerMethod, typeof(ICapabilityModule).GetMethod("Register"));

        var activateMethod = typeBuilder.DefineMethod("ActivateAsync", MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.Virtual, typeof(Task), new[] { typeof(CapabilityActivationContext), typeof(System.Threading.CancellationToken), });
        var activateGenerator = activateMethod.GetILGenerator();
        activateGenerator.Emit(OpCodes.Call, typeof(Task).GetProperty("CompletedTask").GetGetMethod());
        activateGenerator.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(activateMethod, typeof(ICapabilityModule).GetMethod("ActivateAsync"));

        var deactivateMethod = typeBuilder.DefineMethod("DeactivateAsync", MethodAttributes.Public | MethodAttributes.NewSlot | MethodAttributes.Virtual, typeof(Task), new[] { typeof(System.Threading.CancellationToken), });
        var deactivateGenerator = deactivateMethod.GetILGenerator();
        deactivateGenerator.Emit(OpCodes.Call, typeof(Task).GetProperty("CompletedTask").GetGetMethod());
        deactivateGenerator.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(deactivateMethod, typeof(ICapabilityModule).GetMethod("DeactivateAsync"));

        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }

    /// <summary>
    /// 发射一个公共类型引用"未保存的瞬态程序集类型"的程序集：加载后 GetTypes 因依赖缺失抛
    /// <see cref="ReflectionTypeLoadException"/>（复现依赖残缺的能力包形态）。
    /// </summary>
    /// <param name="path">保存路径。</param>
    /// <param name="assemblyIdentityName">程序集 identity 名。</param>
    private static void EmitAssemblyWithUnresolvableReference(string path, string assemblyIdentityName)
    {
        var transientAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CapabilityDeploymentScanProbe.Missing"), AssemblyBuilderAccess.Run);
        var transientModule = transientAssembly.DefineDynamicModule("CapabilityDeploymentScanProbe.Missing");
        var missingType = transientModule.DefineType("CapabilityDeploymentScanProbe.MissingCarrier", TypeAttributes.Public | TypeAttributes.Class);

        var assemblyBuilder = new PersistedAssemblyBuilder(new AssemblyName(assemblyIdentityName), typeof(object).Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyIdentityName);
        var typeBuilder = moduleBuilder.DefineType(assemblyIdentityName + ".DependentType", TypeAttributes.Public | TypeAttributes.Class, typeof(object), new[] { missingType, });
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        typeBuilder.CreateType();
        assemblyBuilder.Save(path);
    }
}
