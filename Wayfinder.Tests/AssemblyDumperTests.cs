using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wayfinder.DependencyResolver;
using Wayfinder.DependencyResolver.Logger;
using Wayfinder.DependencyResolver.Nuget;
using Wayfinder.DependencyResolver.Schemas;

namespace Wayfinder.Tests
{
    [TestClass]
    [DeploymentItem("testdata.zip")]
    public class AssemblyDumperTests
    {
        private static AssemblyInspector _inspector;

        [ClassInitialize]
        public static void TestClassInitialize(TestContext context)
        {
            // Unpack test data folder
            ZipFile.ExtractToDirectory("testdata.zip", Environment.CurrentDirectory, true);
            ILogger logger = new ConsoleLogger();
            _inspector = new AssemblyInspector(logger);
        }

        [ClassCleanup]
        public static void TestClassCleanup()
        {
            try
            {
                DirectoryInfo testDataDir = new DirectoryInfo("testdata");
                if (testDataDir.Exists)
                {
                    testDataDir.Delete(true);
                }
            }
            catch (Exception) { }

            _inspector.Dispose();
        }

        [TestMethod]
        public void TestLoader_BasicManagedDll()
        {
            FileInfo inputFile = new FileInfo("testdata\\binaries\\Durandal.Win32.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            Assert.IsNotNull(parsedData);
            Assert.AreEqual("Durandal.Win32", parsedData.AssemblyBinaryName);
            Assert.AreEqual(".NETFramework,Version=v4.5", parsedData.AssemblyFramework);
            Assert.AreEqual(inputFile.FullName, parsedData.AssemblyFilePath.FullName);
            Assert.AreEqual("Durandal.Win32, Version=20.0.3613.0, Culture=neutral, PublicKeyToken=null", parsedData.AssemblyFullName);
            Assert.AreEqual("89c2e6c5fa443dcb1545d6994d28f49c", parsedData.AssemblyHashMD5);
            Assert.AreEqual(BinaryType.Managed, parsedData.AssemblyType);
            Assert.AreEqual(BinaryPlatform.AnyCPU, parsedData.Platform);
            Assert.AreEqual(new Version("20.0.3613.0"), parsedData.AssemblyVersion);
            Assert.AreEqual("", parsedData.LoaderError);
            Assert.AreEqual(5, parsedData.ReferencedAssemblies.Count);
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) => 
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("1.8.5.0") &&
                string.Equals(s.AssemblyBinaryName, "BouncyCastle.Crypto", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("20.0.3613.0") &&
                string.Equals(s.AssemblyBinaryName, "Durandal", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.0.0") &&
                string.Equals(s.AssemblyBinaryName, "System.Core", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.0.0") &&
                string.Equals(s.AssemblyBinaryName, "System.Numerics", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.0.0") &&
                string.Equals(s.AssemblyBinaryName, "WindowsBase", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void TestLoader_ManagedDllWithPInvoke()
        {
            FileInfo inputFile = new FileInfo("testdata\\binaries\\ManagedBass.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            Assert.IsNotNull(parsedData);
            Assert.AreEqual("ManagedBass", parsedData.AssemblyBinaryName);
            Assert.AreEqual(inputFile.FullName, parsedData.AssemblyFilePath.FullName);
            Assert.AreEqual("ManagedBass, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", parsedData.AssemblyFullName);
            Assert.AreEqual("ede0ea77961db40f8b244d777181be", parsedData.AssemblyHashMD5);
            Assert.AreEqual(BinaryType.Managed, parsedData.AssemblyType);
            Assert.AreEqual(BinaryPlatform.AnyCPU, parsedData.Platform);
            Assert.AreEqual(new Version("1.0.0.0"), parsedData.AssemblyVersion);
            Assert.AreEqual("", parsedData.LoaderError);
            Assert.AreEqual(13, parsedData.ReferencedAssemblies.Count);
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.10.0") &&
                string.Equals(s.AssemblyBinaryName, "System.IO", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.10.0") &&
                string.Equals(s.AssemblyBinaryName, "System.Text.Encoding", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.PInvoke &&
                string.Equals(s.AssemblyBinaryName, "bass", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void TestLoader_NativeDll_x64()
        {
            FileInfo inputFile = new FileInfo("testdata\\binaries\\bass.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            Assert.IsNotNull(parsedData);
            Assert.AreEqual("bass", parsedData.AssemblyBinaryName);
            Assert.AreEqual(inputFile.FullName, parsedData.AssemblyFilePath.FullName);
            Assert.AreEqual("175fd16e804bf2bbe8dd9c1a640f44d", parsedData.AssemblyHashMD5);
            Assert.AreEqual(BinaryType.Native, parsedData.AssemblyType);
            Assert.AreEqual(BinaryPlatform.AMD64, parsedData.Platform);
            Assert.AreEqual("", parsedData.LoaderError);
            Assert.AreEqual(6, parsedData.ReferencedAssemblies.Count);
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "kernel32", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "winmm", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "ole32", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "user32", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "msvcrt", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "msacm32", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void TestLoader_NativeDll_x86()
        {
            FileInfo inputFile = new FileInfo("testdata\\binaries\\EvernoteIE.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            Assert.IsNotNull(parsedData);
            Assert.AreEqual("EvernoteIE", parsedData.AssemblyBinaryName);
            Assert.AreEqual(inputFile.FullName, parsedData.AssemblyFilePath.FullName);
            Assert.AreEqual("f4302d172135b15623fa5b8b799fb5b", parsedData.AssemblyHashMD5);
            Assert.AreEqual(BinaryType.Native, parsedData.AssemblyType);
            Assert.AreEqual(BinaryPlatform.X86, parsedData.Platform);
            Assert.AreEqual("", parsedData.LoaderError);
            Assert.AreEqual(13, parsedData.ReferencedAssemblies.Count);
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "kernel32", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "ole32", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Native &&
                string.Equals(s.AssemblyBinaryName, "xmllite", StringComparison.OrdinalIgnoreCase)));
        }

        [TestMethod]
        public void TestLoader_ResolveNugetPackage()
        {
            NugetPackageCache packageCache = new NugetPackageCache(new DirectoryInfo[]
                {
                    new DirectoryInfo("testdata\\nuget cache")
                });

            FileInfo inputFile = new FileInfo("testdata\\nuget cache\\bond.runtime.csharp\\5.3.1\\lib\\net45\\Bond.JSON.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, packageCache);
            Assert.IsNotNull(parsedData);
            Assert.IsNotNull(parsedData.NugetSourcePackages);
            Assert.AreEqual(1, parsedData.NugetSourcePackages.Count);
            Assert.AreEqual("bond.runtime.csharp", parsedData.NugetSourcePackages[0].PackageName);
            Assert.AreEqual("5.3.1", parsedData.NugetSourcePackages[0].PackageVersion);
        }

        [TestMethod]
        public void TestLoader_ManagedDllWithBindingRedirects()
        {
            FileInfo inputFile = new FileInfo(@"testdata\\binaries\\BasicResolvers.dll");
            AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            Assert.IsNotNull(parsedData);
            Assert.AreEqual("BasicResolvers", parsedData.AssemblyBinaryName);
            Assert.AreEqual(inputFile.FullName, parsedData.AssemblyFilePath.FullName);
            Assert.AreEqual("BasicResolvers, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", parsedData.AssemblyFullName);
            Assert.AreEqual("8f3ff4247c2963d99dc4a58481a380", parsedData.AssemblyHashMD5);
            Assert.AreEqual(BinaryType.Managed, parsedData.AssemblyType);
            Assert.AreEqual(BinaryPlatform.AMD64, parsedData.Platform);
            Assert.AreEqual(new Version("1.0.0.0"), parsedData.AssemblyVersion);
            Assert.AreEqual("", parsedData.LoaderError);
            Assert.AreEqual(8, parsedData.ReferencedAssemblies.Count);
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("9.0.0.0") &&
                s.ReferencedAssemblyVersionAfterBindingOverride == new Version("9.0.0.0") &&
                string.Equals(s.AssemblyBinaryName, "DataSchemas", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("1.0.0.0") &&
                s.ReferencedAssemblyVersionAfterBindingOverride == new Version("1.1.15.0") &&
                s.BindingRedirectCodeBasePath == "OverrideDialogHelpers/DialogHelpers.dll" &&
                string.Equals(s.AssemblyBinaryName, "DialogHelpers", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("12.0.0.0") &&
                s.ReferencedAssemblyVersionAfterBindingOverride == new Version("12.0.0.5") &&
                string.Equals(s.AssemblyBinaryName, "Newtonsoft.Json", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(parsedData.ReferencedAssemblies.Any((s) =>
                s.ReferenceType == AssemblyReferenceType.Managed &&
                s.ReferencedAssemblyVersion == new Version("4.0.0.0") &&
                s.ReferencedAssemblyVersionAfterBindingOverride == new Version("4.0.0.1") &&
                string.Equals(s.AssemblyBinaryName, "System.Core", StringComparison.OrdinalIgnoreCase)));
        }

        [Ignore]
        [TestMethod]
        public void TestSandbox()
        {
            //FileInfo inputFile = new FileInfo("testdata\\binaries\\BasicResolvers.dll");
            //AssemblyData parsedData = _inspector.InspectSingleAssembly(inputFile, null);
            //Assert.IsNotNull(parsedData);

            NugetPackageCache packageCache = new NugetPackageCache();
            DirectoryInfo inputDir = new DirectoryInfo(@"C:\Code\WebCrawler\bin");
            ISet<DependencyGraphNode> graph = _inspector.BuildDependencyGraph(inputDir, packageCache);
            Assert.IsNotNull(graph);
        }
    }
}
