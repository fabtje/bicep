// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Containers.ContainerRegistry.Specialized;
using Bicep.Core.Registry.Auth;
using Bicep.Core.Tracing;
using System;

namespace Bicep.Core.Registry
{
    public class ContainerRegistryClientFactory : IContainerRegistryClientFactory
    {
        private readonly ITokenCredentialFactory credentialFactory;

        public ContainerRegistryClientFactory(ITokenCredentialFactory credentialFactory)
        {
            this.credentialFactory = credentialFactory;
        }

        public ContainerRegistryBlobClient CreateBlobClient(RootConfiguration configuration, Uri registryUri, string repository)
        {
            var options = new ContainerRegistryClientOptions();
            options.Diagnostics.ApplySharedContainerRegistrySettings();
            options.AuthenticationScope = configuration.Cloud.AuthenticationScope;

            var credential = this.credentialFactory.CreateChain(configuration.Cloud.CredentialPrecedence, configuration.Cloud.ActiveDirectoryAuthorityUri);

            // TODO: Add support for national clouds
            options.Audience = ContainerRegistryAudience.AzureResourceManagerPublicCloud;

            return new(registryUri, credential, repository, options);
        }
    }
}
