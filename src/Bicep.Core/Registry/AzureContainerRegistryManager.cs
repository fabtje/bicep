// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Containers.ContainerRegistry.Specialized;
using Azure.Core;
using Bicep.Core.Modules;
using Bicep.Core.Registry.Oci;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Bicep.Core.Registry
{
    public class AzureContainerRegistryManager
    {
        // media types are case-insensitive (they are lowercase by convention only)
        private const StringComparison MediaTypeComparison = StringComparison.OrdinalIgnoreCase;
        private const StringComparison DigestComparison = StringComparison.Ordinal;

        private readonly TokenCredential tokenCredential;
        private readonly IContainerRegistryClientFactory clientFactory;

        public AzureContainerRegistryManager(TokenCredential tokenCredential, IContainerRegistryClientFactory clientFactory)
        {
            this.tokenCredential = tokenCredential;
            this.clientFactory = clientFactory;
        }

        public async Task<OciArtifactResult> PullArtifactAsync(Configuration.RootConfiguration configuration, OciArtifactModuleReference moduleReference)
        {
            var client = this.CreateBlobClient(configuration, moduleReference);
            var (manifest, manifestStream, manifestDigest) = await DownloadManifestAsync(moduleReference, client);

            var moduleStream = await ProcessManifest(client, manifest);

            return new OciArtifactResult(manifestDigest, manifest, manifestStream, moduleStream);
        }

        public async Task PushArtifactAsync(Configuration.RootConfiguration configuration, OciArtifactModuleReference moduleReference, StreamDescriptor config, params StreamDescriptor[] layers)
        {
            // TODO: How do we choose this? Does it ever change?
            var algorithmIdentifier = DescriptorFactory.AlgorithmIdentifierSha256;

            var blobClient = this.CreateBlobClient(configuration, moduleReference);

            config.ResetStream();
            var manifest = new Azure.Containers.ContainerRegistry.Specialized.OciManifest
            {
                SchemaVersion = 2,
                Config = DescriptorFactory.CreateSdkDescriptor(algorithmIdentifier, config)
            };

            config.ResetStream();
            await blobClient.UploadBlobAsync(config.Stream);

            foreach (var layer in layers)
            {
                layer.ResetStream();
                var layerDescriptor = DescriptorFactory.CreateSdkDescriptor(algorithmIdentifier, layer);
                manifest.Layers.Add(layerDescriptor);

                layer.ResetStream();
                await blobClient.UploadBlobAsync(layer.Stream);
            }            

            // BUG: the client closes the stream :(
            await blobClient.UploadManifestAsync(manifest, new UploadManifestOptions { Tag = moduleReference.Tag });
        }

        private static Uri GetRegistryUri(OciArtifactModuleReference moduleReference) => new Uri($"https://{moduleReference.Registry}");

        private ContainerRegistryBlobClient CreateBlobClient(Configuration.RootConfiguration configuration, OciArtifactModuleReference moduleReference) => this.clientFactory.CreateBlobClient(configuration, GetRegistryUri(moduleReference), moduleReference.Repository);

        private static async Task<(Oci.OciManifest, Stream, string)> DownloadManifestAsync(OciArtifactModuleReference moduleReference, ContainerRegistryBlobClient client)
        {
            Response<DownloadManifestResult> manifestResponse;
            try
            {
                var options = new DownloadManifestOptions(moduleReference.Tag);
                manifestResponse = await client.DownloadManifestAsync(options);
            }
            catch(RequestFailedException exception) when (exception.Status == 404)
            {
                // manifest does not exist
                throw new OciModuleRegistryException("The module does not exist in the registry.", exception);
            }

            var stream = ValidateManifestResponse(manifestResponse);

            // the SDK doesn't expose all the manifest properties we need
            // so we need to deserialize the manifest ourselves to get everything
            var deserialized = DeserializeManifest(stream);
            stream.Position= 0;

            return (deserialized, stream, manifestResponse.Value.Digest);
        }

        private static Stream ValidateManifestResponse(Response<DownloadManifestResult> manifestResponse)
        {
            var digestFromRegistry = manifestResponse.Value.Digest;

            var stream = manifestResponse.GetRawResponse().ContentStream;
            if(stream is null)
            {
                throw new OciModuleRegistryException($"Manifest stream is null.");
            }

            stream.Position = 0;

            // TODO: The registry may use a different digest algorithm - we need to handle that
            string digestFromContent = DescriptorFactory.ComputeDigest(DescriptorFactory.AlgorithmIdentifierSha256, stream);

            if (!string.Equals(digestFromRegistry, digestFromContent, DigestComparison))
            {
                throw new OciModuleRegistryException($"There is a mismatch in the manifest digests. Received content digest = {digestFromContent}, Digest in registry response = {digestFromRegistry}");
            }

            stream.Position = 0;
            return stream;
        }

        private static async Task<Stream> ProcessManifest(ContainerRegistryBlobClient client, Oci.OciManifest manifest)
        {
            ProcessConfig(manifest.Config);
            if (manifest.Layers.Length != 1)
            {
                throw new InvalidModuleException("Expected a single layer in the OCI artifact.");
            }

            var layer = manifest.Layers.Single();

            return await ProcessLayer(client, layer);
        }

        private static void ValidateBlobResponse(Response<DownloadBlobResult> blobResponse, OciDescriptor descriptor)
        {
            var stream = blobResponse.Value.Content;

            if(descriptor.Size != stream.Length)
            {
                throw new InvalidModuleException($"Expected blob size of {descriptor.Size} bytes but received {stream.Length} bytes from the registry.");
            }

            stream.Position = 0;
            string digestFromContents = DescriptorFactory.ComputeDigest(DescriptorFactory.AlgorithmIdentifierSha256, stream);
            stream.Position = 0;

            if(!string.Equals(descriptor.Digest, digestFromContents, DigestComparison))
            {
                throw new InvalidModuleException($"There is a mismatch in the layer digests. Received content digest = {digestFromContents}, Requested digest = {descriptor.Digest}");
            }
        }

        private static async Task<Stream> ProcessLayer(ContainerRegistryBlobClient client, OciDescriptor layer)
        {
            if(!string.Equals(layer.MediaType, BicepMediaTypes.BicepModuleLayerV1Json, MediaTypeComparison))
            {
                throw new InvalidModuleException($"Did not expect layer media type \"{layer.MediaType}\".");
            }

            Response<DownloadBlobResult> blobResult;
            try
            {
                blobResult = await client.DownloadBlobAsync(layer.Digest);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                throw new InvalidModuleException($"Module manifest refers to a non-existent blob with digest \"{layer.Digest}\".", exception);
            }

            ValidateBlobResponse(blobResult, layer);

            return blobResult.Value.Content;
        }

        private static void ProcessConfig(OciDescriptor config)
        {
            // media types are case insensitive
            if(!string.Equals(config.MediaType, BicepMediaTypes.BicepModuleConfigV1, MediaTypeComparison))
            {
                throw new InvalidModuleException($"Did not expect config media type \"{config.MediaType}\".");
            }

            if(config.Size != 0)
            {
                throw new InvalidModuleException("Expected an empty config blob.");
            }
        }

        private static Oci.OciManifest DeserializeManifest(Stream stream)
        {
            try
            {
                return OciSerialization.Deserialize<Oci.OciManifest>(stream);
            }
            catch(Exception exception)
            {
                throw new InvalidModuleException("Unable to deserialize the module manifest.", exception);
            }
        }

        private class InvalidModuleException : OciModuleRegistryException
        {
            public InvalidModuleException(string innerMessage) : base($"The OCI artifact is not a valid Bicep module. {innerMessage}")
            {
            }

            public InvalidModuleException(string innerMessage, Exception innerException)
                : base($"The OCI artifact is not a valid Bicep module. {innerMessage}", innerException)
            {
            }
        }
    }
}
