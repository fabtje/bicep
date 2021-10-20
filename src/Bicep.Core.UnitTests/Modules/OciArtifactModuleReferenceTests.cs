// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.Configuration;
using Bicep.Core.Modules;
using Bicep.Core.UnitTests.Assertions;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace Bicep.Core.UnitTests.Modules
{
    [TestClass]
    public class OciArtifactModuleReferenceTests
    {
        public const string ExampleTagOfMaxLength = "abcdefghijklmnopqrstuvxyz0123456789._-._-._-._-ABCDEFGHIJKLMNOPQRSTUVXYZ0123456789._-._-._-._-abcdefghijklmnopqrstuvxyz012345678";

        public const string ExampleRepositoryOfMaxLength = "abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abc";

        public const string ExampleRegistryOfMaxLength = "abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abc";

        public const string ExamplePathSegment1 = "abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789-abcdefghijklmnopqrstuvxyz0123456789_abcdefghijklmnopqrstuvxyz0123456789";

        public const string ExamplePathSegment2 = "a.b-0_1";

        public record ValidCase(string Value, string ExpectedRegistry, string ExpectedRepository, string ExpectedTag);

        [TestMethod]
        public void ExamplesShouldMatchExpectedConstraints()
        {
            ExampleTagOfMaxLength.Should().HaveLength(128);
            ExampleRepositoryOfMaxLength.Should().HaveLength(255);
            ExampleRegistryOfMaxLength.Should().HaveLength(255);
        }

        [DynamicData(nameof(GetValidCases), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(GetDisplayName))]
        [DataTestMethod]
        public void ValidReferencesShouldParseCorrectly(ValidCase @case)
        {
            var parsed = Parse(@case.Value);

            using (new AssertionScope())
            {
                parsed.Registry.Should().Be(@case.ExpectedRegistry);
                parsed.Repository.Should().Be(@case.ExpectedRepository);
                parsed.Tag.Should().Be(@case.ExpectedTag);
                parsed.ArtifactId.Should().Be(@case.Value);
            }
        }

        [DynamicData(nameof(GetValidCases), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(GetDisplayName))]
        [DataTestMethod]
        public void ValidReferenceShouldBeEqualToItself(ValidCase @case)
        {
            var first = Parse(@case.Value);
            var second = Parse(@case.Value);

            first.Equals(second).Should().Be(true);
            first.GetHashCode().Should().Be(second.GetHashCode());
        }

        [DynamicData(nameof(GetValidCases), DynamicDataSourceType.Method, DynamicDataDisplayName = nameof(GetDisplayName))]
        [DataTestMethod]
        public void ValidReferenceShouldBeUriParseable(ValidCase @case)
        {
            var parsed = Parse(@case.Value);

            // the go2def flow in the language server passes the reference through URIs
            // in cases of documents that come from the local module cache
            FluentActions.Invoking(() => new Uri(parsed.FullyQualifiedReference)).Should().NotThrow();

            // the unqualified reference should be parseable as a URI segment as well
            FluentActions.Invoking(() => new Uri("test://" + parsed.UnqualifiedReference)).Should().NotThrow();
        }

        // bad
        [DataRow("", "BCP193", "The specified OCI artifact reference \"br:\" is not valid. Specify a reference in the format of \"br:<artifact-uri>:<tag>\", or \"br/<module-alias>:<module-name-or-path>:<tag>\".")]
        [DataRow("a", "BCP193", "The specified OCI artifact reference \"br:a\" is not valid. Specify a reference in the format of \"br:<artifact-uri>:<tag>\", or \"br/<module-alias>:<module-name-or-path>:<tag>\".")]
        [DataRow("a/", "BCP193", "The specified OCI artifact reference \"br:a/\" is not valid. Specify a reference in the format of \"br:<artifact-uri>:<tag>\", or \"br/<module-alias>:<module-name-or-path>:<tag>\".")]
        [DataRow("a/b", "BCP196", "The specified OCI artifact reference \"br:a/b\" is not valid. The module tag is missing.")]
        [DataRow("a/b:", "BCP196", "The specified OCI artifact reference \"br:a/b:\" is not valid. The module tag is missing.")]
        [DataRow("a/b:$", "BCP198", "The specified OCI artifact reference \"br:a/b:$\" is not valid. The tag \"$\" is not valid. Valid characters are alphanumeric, \".\", \"_\", or \"-\" but the tag cannot begin with \".\", \"_\", or \"-\".")]
        [DataRow("example.com/hello.", "BCP195", "The specified OCI artifact reference \"br:example.com/hello.\" is not valid. The module path segment \"hello.\" is not valid. Each module name path segment must be a lowercase alphanumeric string optionally separated by a \".\", \"_\" , or \"-\".")]
        [DataRow("example.com/hello./there", "BCP195", "The specified OCI artifact reference \"br:example.com/hello./there\" is not valid. The module path segment \"hello.\" is not valid. Each module name path segment must be a lowercase alphanumeric string optionally separated by a \".\", \"_\" , or \"-\".")]
        [DataRow("example.com/hello./there:v1", "BCP195", "The specified OCI artifact reference \"br:example.com/hello./there:v1\" is not valid. The module path segment \"hello.\" is not valid. Each module name path segment must be a lowercase alphanumeric string optionally separated by a \".\", \"_\" , or \"-\".")]
        [DataRow("example.com/hello/there^", "BCP195", "The specified OCI artifact reference \"br:example.com/hello/there^\" is not valid. The module path segment \"there^\" is not valid. Each module name path segment must be a lowercase alphanumeric string optionally separated by a \".\", \"_\" , or \"-\".")]
        [DataRow("example.com/hello^/there:v1", "BCP195", "The specified OCI artifact reference \"br:example.com/hello^/there:v1\" is not valid. The module path segment \"hello^\" is not valid. Each module name path segment must be a lowercase alphanumeric string optionally separated by a \".\", \"_\" , or \"-\".")]
        [DataRow("test.azurecr.io/foo/bar:" + ExampleTagOfMaxLength + "a", "BCP197", "The specified OCI artifact reference \"br:test.azurecr.io/foo/bar:abcdefghijklmnopqrstuvxyz0123456789._-._-._-._-ABCDEFGHIJKLMNOPQRSTUVXYZ0123456789._-._-._-._-abcdefghijklmnopqrstuvxyz012345678a\" is not valid. The tag \"abcdefghijklmnopqrstuvxyz0123456789._-._-._-._-ABCDEFGHIJKLMNOPQRSTUVXYZ0123456789._-._-._-._-abcdefghijklmnopqrstuvxyz012345678a\" exceeds the maximum length of 128 characters.")]
        [DataRow("example.com/" + ExampleRepositoryOfMaxLength + "a:v3", "BCP199", "The specified OCI artifact reference \"br:example.com/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abca:v3\" is not valid. Module path \"abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abcdefghijklmnopqrstuvxyz0123456789/abca\" exceeds the maximum length of 255 characters.")]
        [DataRow(ExampleRegistryOfMaxLength + "a/hello/there:1.0", "BCP200", "The specified OCI artifact reference \"br:abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abca/hello/there:1.0\" is not valid. The registry \"abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abcdefghijklmnopqrstuvxyz0123456789.abca\" exceeds the maximum length of 255 characters.")]
        [DataTestMethod]
        public void InvalidReferencesShouldProduceExpectedError(string value, string expectedCode, string expectedError)
        {
            OciArtifactModuleReference.TryParse(null, value, BicepTestConstants.BuiltInConfigurationWithAnalyzersDisabled, out var failureBuilder).Should().BeNull();
            failureBuilder!.Should().NotBeNull();

            using (new AssertionScope())
            {
                failureBuilder!.Should().HaveCode(expectedCode);
                failureBuilder!.Should().HaveMessage(expectedError);
            }
        }

        [DataRow("TEST.azurecr.IO/foo/bar:latest", "test.azurecr.io/foo/bar:latest")]
        [DataRow("LOCALHOST:5000/test/ssss:v1", "localhost:5000/test/ssss:v1")]
        [DataTestMethod]
        public void ReferencesWithRegistryCasingDifferencesShouldBeEqual(string package1, string package2)
        {
            var (first, second) = ParsePair(package1, package2);

            first.Equals(second).Should().BeTrue();
            first.GetHashCode().Should().Be(second.GetHashCode());
        }

        [DataRow("test.azurecr.io/foo/bar:latest", "test.azurecr.io/foo/bar:LATEST")]
        [DataRow("localhost:5000/test/ssss:version1", "localhost:5000/test/ssss:VERSION1")]
        [DataRow("one.azurecr.io/first/second:tag1","two.azurecr.io/third/fourth:tag2")]
        [DataTestMethod]
        public void MismatchedReferencesShouldNotBeEqual(string package1, string package2)
        {
            var (first, second) = ParsePair(package1, package2);
            first.Equals(second).Should().BeFalse();
            first.GetHashCode().Should().NotBe(second.GetHashCode());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow("****")]
        [DataRow("/")]
        [DataRow(":")]
        [DataRow("foo bar ÄÄÄ")]
        public void TryParse_InvalidAliasName_ReturnsNullAndSetsErrorDiagnostic(string aliasName)
        {
            var reference = OciArtifactModuleReference.TryParse(aliasName, "", BicepTestConstants.BuiltInConfiguration, out var errorBuilder);

            reference.Should().BeNull();
            errorBuilder!.Should().HaveCode("BCP211");
            errorBuilder!.Should().HaveMessage($"The module alias name \"{aliasName}\" is invalid. Valid characters are alphanumeric, \"_\", or \"-\".");
        }

        [DataTestMethod]
        [DataRow("myRegistry", "path/to/module:v1", null, "BCP213", "The OCI artifact module alias name \"myRegistry\" does not exist in the built-in Bicep configuration.")]
        [DataRow("myModulePath", "myModule:v2", "bicepconfig.json", "BCP213", "The OCI artifact module alias name \"myModulePath\" does not exist in the Bicep configuration \"bicepconfig.json\".")]
        public void TryParse_AliasNotInConfiguration_ReturnsNullAndSetsError(string aliasName, string referenceValue, string? configurationPath, string expectedCode, string expectedMessage)
        {
            var configuration = BicepTestConstants.CreateMockConfiguration(configurationPath: configurationPath);

            var reference = OciArtifactModuleReference.TryParse(aliasName, referenceValue, configuration, out var errorBuilder);

            reference.Should().BeNull();
            ((object?)errorBuilder).Should().NotBeNull();
            errorBuilder!.Should().HaveCode(expectedCode);
            errorBuilder!.Should().HaveMessage(expectedMessage);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetInvalidAliasData), DynamicDataSourceType.Method)]
        public void TryParse_InvalidAlias_ReturnsNullAndSetsError(string aliasName, string referenceValue, RootConfiguration configuration, string expectedCode, string expectedMessage)
        {
            var reference = OciArtifactModuleReference.TryParse(aliasName, referenceValue, configuration, out var errorBuilder);

            reference.Should().BeNull();
            ((object?)errorBuilder).Should().NotBeNull();
            errorBuilder!.Should().HaveCode(expectedCode);
            errorBuilder!.Should().HaveMessage(expectedMessage);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetValidAliasData), DynamicDataSourceType.Method)]
        public void TryGetModuleReference_ValidAlias_ReplacesReferenceValue(string aliasName,  string referenceValue, string fullyQualifiedReferenceValue, RootConfiguration configuration)
        {
            var reference = OciArtifactModuleReference.TryParse(aliasName, referenceValue, configuration, out var errorBuilder);

            reference.Should().NotBeNull();
            reference!.FullyQualifiedReference.Should().Be(fullyQualifiedReferenceValue);
        }


        private static OciArtifactModuleReference Parse(string package)
        {
            var parsed = OciArtifactModuleReference.TryParse(null, package, BicepTestConstants.BuiltInConfigurationWithAnalyzersDisabled, out var failureBuilder);
            failureBuilder!.Should().BeNull();
            parsed.Should().NotBeNull();
            return parsed!;
        }

        private static (OciArtifactModuleReference, OciArtifactModuleReference) ParsePair(string first, string second) => (Parse(first), Parse(second));

        private static IEnumerable<object[]> GetValidCases()
        {
            static object[] CreateRow(string value, string expectedRegistry, string expectedRepository, string expectedTag) =>
                new object[] { new ValidCase(value, expectedRegistry, expectedRepository, expectedTag) };

            yield return CreateRow("a/b:C", "a", "b", "C");
            yield return CreateRow("localhost/hello:V1", "localhost", "hello", "V1");
            yield return CreateRow("localhost:123/hello:V1", "localhost:123", "hello", "V1");
            yield return CreateRow("test.azurecr.io/foo/bar:latest", "test.azurecr.io", "foo/bar", "latest");
            yield return CreateRow("test.azurecr.io/foo/bar:" + ExampleTagOfMaxLength, "test.azurecr.io", "foo/bar", ExampleTagOfMaxLength);
            yield return CreateRow("example.com/" + ExamplePathSegment1 + "/" + ExamplePathSegment2 + ":1", "example.com", ExamplePathSegment1 + "/" + ExamplePathSegment2, "1");
            yield return CreateRow("example.com/" + ExampleRepositoryOfMaxLength + ":v3", "example.com", ExampleRepositoryOfMaxLength, "v3");
            yield return CreateRow(ExampleRegistryOfMaxLength + "/hello/there:1.0", ExampleRegistryOfMaxLength, "hello/there", "1.0");
        }

        private static IEnumerable<object[]> GetInvalidAliasData()
        {
            yield return new object[]
            {
                "myModulePath",
                "myModule:v1",
                BicepTestConstants.CreateMockConfiguration(
                    new()
                    {
                        ["moduleAliases.br.myModulePath.modulePath"] = "path",
                    }),
                "BCP216",
                "The OCI artifact module alias \"myModulePath\" in the built-in Bicep configuration is invalid. The \"registry\" property cannot be null or undefined.",
            };

            yield return new object[]
            {
                "myModulePath2",
                "myModule:v2",
                BicepTestConstants.CreateMockConfiguration(
                    new()
                    {
                        ["moduleAliases.br.myModulePath2.modulePath"] = "path2",
                    },
                    "bicepconfig.json"),
                "BCP216",
                "The OCI artifact module alias \"myModulePath2\" in the Bicep configuration \"bicepconfig.json\" is invalid. The \"registry\" property cannot be null or undefined.",
            };
        }

        private static IEnumerable<object[]> GetValidAliasData()
        {
            yield return new object[]
            {
                "myModulePath",
                "mymodule:v1",
                "br:example.com/path/mymodule:v1",
                BicepTestConstants.CreateMockConfiguration(new()
                {
                    ["moduleAliases.br.myModulePath.registry"] = "example.com",
                    ["moduleAliases.br.myModulePath.modulePath"] = "path",
                }),
            };

            yield return new object[]
            {
                "myModulePath2",
                "mymodule:v2",
                "br:localhost:8000/root/parent/mymodule:v2",
                BicepTestConstants.CreateMockConfiguration(
                    new()
                    {
                        ["moduleAliases.br.myModulePath2.registry"] = "localhost:8000",
                        ["moduleAliases.br.myModulePath2.modulePath"] = "root/parent",
                    },
                    "bicepconfig.json"),
            };
        }


        public static string GetDisplayName(MethodInfo info, object[] data) => $"{info.Name}_{((ValidCase)data[0]).Value}";
    }
}
