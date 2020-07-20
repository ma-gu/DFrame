﻿using System;
using System.Threading;
using System.Threading.Tasks;
using DFrame;

namespace DFrame.KubernetesWorker
{
    public enum ScalingType
    {
        Deployment,
        Job,
    }

    public class KubernetesScalingProvider : IScalingProvider
    {
        public ScalingType ScalingType { get; } = ScalingType.Job;

        private readonly KubernetesApi _kubeapi;
        private readonly bool preserveWorker = false; // not recommended

        // manifest configuable parameters
        private readonly string _ns = "dframe";
        private readonly string _name = "dframe-worker";
        private readonly string connectTo = Environment.GetEnvironmentVariable("DFRAME_MASTER_HOST") ?? $"dframe-master.dframe.svc.cluster.local";
        private readonly string image = Environment.GetEnvironmentVariable("WORKER_IMAGE_NAME") ?? "";
        private readonly string imageTag = Environment.GetEnvironmentVariable("WORKER_IMAGE_TAG") ?? "";
        private readonly string imagePullSecret = Environment.GetEnvironmentVariable("WORKER_IMAGE_PULL_SECERT") ?? "";
        private readonly string imagePullPolicy = Environment.GetEnvironmentVariable("WORKER_IMAGE_PULL_POLICY") ?? "IfNotPresent";

        public KubernetesScalingProvider()
        {
            _kubeapi = new KubernetesApi(new KubernetesApiConfig
            {
                ResponseHeaderType = HeaderContentType.Yaml,
                SkipCertificateValidation = true,
            });
        }

        public KubernetesScalingProvider(ScalingType scalingType) : base()
        {
            this.ScalingType = scalingType;
        }

        public async Task StartWorkerAsync(DFrameOptions options, int nodeCount, IServiceProvider provider, CancellationToken cancellationToken)
        {
            Console.WriteLine($"scale out workers. {_ns}/{_name} {ScalingType}");

            // create workers via kubernetes client api.
            // todo: rbac が有効だと service account / role / rolebindings が必要 (role は namespace/deployments/pod の create権限....)

            // todo: create namespace for the worker. default same namespace.
            //var namespaceManifest = KubernetesManifest.GetNamespace(_ns);
            //if (!await _kubeapi.ExistsNamespaceAsync(_ns))
            //{
            //    _ = await _kubeapi.CreateNamespaceAsync(_ns, namespaceManifest, cancellationToken);
            //}

            // todo: node count を pod とみなしているので、node = vm の修正が必要。
            // その場合、node = k8s node, worker = deploy replicas (job parallism).
            switch (ScalingType)
            {
                case ScalingType.Deployment:
                    await CreateDeployment(image, imageTag, connectTo, nodeCount, cancellationToken);
                    break;
                case ScalingType.Job:
                    await CreateJobAsync(image, imageTag, connectTo, nodeCount, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ScalingType));
            }
        }

        public async ValueTask DisposeAsync()
        {
            Console.WriteLine($"scale in workers. {_ns}/{_name} {ScalingType}");

            // delete worker resource. namespace は master を含むので残す。
            switch (ScalingType)
            {
                case ScalingType.Deployment:
                    if (!preserveWorker && await _kubeapi.ExistsDeploymentAsync(_ns, _name))
                    {
                        await _kubeapi.DeleteDeploymentAsync(_ns, _name);
                    };
                    break;
                case ScalingType.Job:
                    if (!preserveWorker && await _kubeapi.ExistsJobAsync(_ns, _name))
                    {
                        await _kubeapi.DeleteJobAsync(_ns, _name, graceperiodSecond:10);
                    };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ScalingType));
            }
        }

        /// <summary>
        /// create kubernetes job. (recommended)
        /// retry will not happen when worker cause error on scenario.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="imageTag"></param>
        /// <param name="connectTo"></param>
        /// <param name="nodeCount"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask CreateJobAsync(string image, string imageTag, string connectTo, int nodeCount, CancellationToken cancellationToken)
        {
            Console.WriteLine($"use job. {_ns}/{_name}");
            var jobManifest = KubernetesManifest.GetJob(_name, image, imageTag, connectTo, imagePullPolicy, imagePullSecret, nodeCount);
            Console.WriteLine(jobManifest);
            _ = await _kubeapi.CreateJobAsync(_ns, jobManifest, cancellationToken);

            // wait kubernetes deployments done.
            var result = await _kubeapi.GetJobAsync(_ns, _name);
            Console.WriteLine(result);
        }

        /// <summary>
        /// create kubernetes deployment. (not recommended)
        /// retry will happen when worker cause error on scenario. not recommeneded.
        /// </summary>
        /// <param name="image"></param>
        /// <param name="imageTag"></param>
        /// <param name="connectTo"></param>
        /// <param name="nodeCount"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask CreateDeployment(string image, string imageTag, string connectTo, int nodeCount, CancellationToken cancellationToken)
        {
            Console.WriteLine($"use deployment. {_ns}/{_name}");
            var deploymentManifest = KubernetesManifest.GetDeployment(_name, image, imageTag, connectTo, imagePullPolicy, imagePullSecret, nodeCount);
            Console.WriteLine(deploymentManifest);
            _ = await _kubeapi.CreateDeploymentAsync(_ns, deploymentManifest, cancellationToken);

            // wait kubernetes deployments done.
            var result = await _kubeapi.GetDeploymentAsync(_ns, _name);
            Console.WriteLine(result);
        }
    }
}