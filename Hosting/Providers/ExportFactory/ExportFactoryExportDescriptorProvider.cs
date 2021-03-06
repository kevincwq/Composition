﻿// -----------------------------------------------------------------------
// Copyright © Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Composition.Hosting.Core;
using System.Composition.Hosting.Util;
using System.Linq;
using System.Reflection;

namespace System.Composition.Hosting.Providers.ExportFactory
{
    class ExportFactoryExportDescriptorProvider : ExportDescriptorProvider
    {
        static readonly MethodInfo GetExportFactoryDefinitionsMethod = typeof(ExportFactoryExportDescriptorProvider).GetTypeInfo().GetDeclaredMethod("GetExportFactoryDescriptors");

        public override IEnumerable<ExportDescriptorPromise> GetExportDescriptors(CompositionContract exportKey, DependencyAccessor definitionAccessor)
        {
            if (!exportKey.ContractType.IsConstructedGenericType || exportKey.ContractType.GetGenericTypeDefinition() != typeof(ExportFactory<>))
                return NoExportDescriptors;

            var gld = GetExportFactoryDefinitionsMethod.MakeGenericMethod(exportKey.ContractType.GenericTypeArguments[0]);
            var gldm = gld.CreateStaticDelegate<Func<CompositionContract, DependencyAccessor, object>>();
            return (ExportDescriptorPromise[])gldm(exportKey, definitionAccessor);
        }

        static ExportDescriptorPromise[] GetExportFactoryDescriptors<TProduct>(CompositionContract exportFactoryContract, DependencyAccessor definitionAccessor)
        {
            var productContract = exportFactoryContract.ChangeType(typeof(TProduct));
            var boundaries = new string[0];

            IEnumerable<string> specifiedBoundaries;
            CompositionContract unwrapped;
            if (exportFactoryContract.TryUnwrapMetadataConstraint(Constants.SharingBoundaryImportMetadataConstraintName, out specifiedBoundaries, out unwrapped))
            {
                productContract = unwrapped.ChangeType(typeof(TProduct));
                boundaries = (specifiedBoundaries ?? new string[0]).ToArray();
            }

            return definitionAccessor.ResolveDependencies("product", productContract, false)
                .Select(d => new ExportDescriptorPromise(
                    exportFactoryContract,
                    Formatters.Format(typeof(ExportFactory<TProduct>)),
                    false,
                    () => new[] { d },
                    _ =>
                    {
                        var dsc = d.Target.GetDescriptor();
                        var da = dsc.Activator;
                        return ExportDescriptor.Create((c, o) =>
                            {
                                return new ExportFactory<TProduct>(() =>
                                {
                                    var lifetimeContext = new LifetimeContext(c, boundaries);
                                    return Tuple.Create<TProduct, Action>((TProduct)CompositionOperation.Run(lifetimeContext, da), lifetimeContext.Dispose);
                                });
                            },
                            dsc.Metadata);
                    }))
                .ToArray();
        }
    }
}
