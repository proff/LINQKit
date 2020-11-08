using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace LinqKit.Microsoft.EntityFrameworkCore3
{
    public static class LinqKitConfigurationExtensions
    {
        public static DbContextOptionsBuilder UseLinqKit(this DbContextOptionsBuilder optionsBuilder)
        {
            Type type = null;
            if (optionsBuilder.Options.FindExtension<CoreOptionsExtension>()?.ReplacedServices
#if EFCORE5
                ?.TryGetValue((typeof(IQueryTranslationPreprocessorFactory), null), out type) == true)
#else
                ?.TryGetValue(typeof(IQueryTranslationPreprocessorFactory), out type) == true)
#endif                
            {
                var extension = optionsBuilder.Options.FindExtension<LinqKitOptionsExtension>() ??
                                new LinqKitOptionsExtension();
                extension.PreviousQueryTranslationPreprocessorFactory = type;
                ((IDbContextOptionsBuilderInfrastructure) optionsBuilder).AddOrUpdateExtension(extension);
            }

            optionsBuilder
                .ReplaceService<IQueryTranslationPreprocessorFactory, LinqKitQueryTranslationPreprocessorFactory>();
            return optionsBuilder;
        }

        private class LinqKitOptionsExtension : IDbContextOptionsExtension
        {
            private ExtensionInfo _info;
            public Type PreviousQueryTranslationPreprocessorFactory { get; set; }

            public void ApplyServices(IServiceCollection services)
            {
            }

            public void Validate(IDbContextOptions options)
            {
            }

            public DbContextOptionsExtensionInfo Info => _info = _info ?? new ExtensionInfo(this);

            private class ExtensionInfo : DbContextOptionsExtensionInfo
            {
                public ExtensionInfo(LinqKitOptionsExtension extension) : base(extension)
                {
                }

                private new LinqKitOptionsExtension Extension => (LinqKitOptionsExtension) base.Extension;

                public override bool IsDatabaseProvider => false;
                public override string LogFragment => string.Empty;

                public override long GetServiceProviderHashCode()
                {
                    return Extension.PreviousQueryTranslationPreprocessorFactory?.GetHashCode() ?? 0L;
                }

                public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
                {
                    if (Extension.PreviousQueryTranslationPreprocessorFactory != null)
                        debugInfo["LinqKit:" + nameof(PreviousQueryTranslationPreprocessorFactory)] = Extension
                            .PreviousQueryTranslationPreprocessorFactory.GetHashCode()
                            .ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private class LinqKitQueryTranslationPreprocessor : QueryTranslationPreprocessor
        {
            private readonly QueryTranslationPreprocessor _next;

            public LinqKitQueryTranslationPreprocessor(QueryTranslationPreprocessorDependencies dependencies,
                QueryCompilationContext queryCompilationContext, QueryTranslationPreprocessor next) : base(dependencies,
                queryCompilationContext)
            {
                _next = next;
            }

            public override Expression Process(Expression query)
            {
                // base method will be called in _next
                return _next.Process(query.Expand());
            }
        }

        private class LinqKitQueryTranslationPreprocessorFactory : IQueryTranslationPreprocessorFactory
        {
            private readonly QueryTranslationPreprocessorDependencies _dependencies;

            private readonly ConcurrentDictionary<Type, IQueryTranslationPreprocessorFactory> _factories =
                new ConcurrentDictionary<Type, IQueryTranslationPreprocessorFactory>();

            private readonly IServiceProvider _serviceProvider;

            public LinqKitQueryTranslationPreprocessorFactory(QueryTranslationPreprocessorDependencies dependencies,
                RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
                IServiceProvider serviceProvider)
            {
                _dependencies = dependencies;
                _serviceProvider = serviceProvider;
            }


            public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
            {
                var options = queryCompilationContext.ContextOptions.FindExtension<LinqKitOptionsExtension>();
                var type = options?.PreviousQueryTranslationPreprocessorFactory ??
                           typeof(RelationalQueryTranslationPreprocessorFactory);
                var factory = _factories.GetOrAdd(type, t => GetService(t));
                return new LinqKitQueryTranslationPreprocessor(_dependencies, queryCompilationContext,
                    factory.Create(queryCompilationContext));
            }

            private IQueryTranslationPreprocessorFactory GetService(Type t)
            {
                var constructorInfo = t.GetConstructors().OrderByDescending(a => a.GetParameters().Length).First();
                var factory = constructorInfo.Invoke(constructorInfo.GetParameters()
                    .Select(a => _serviceProvider.GetRequiredService(a.ParameterType)).ToArray());
                return (IQueryTranslationPreprocessorFactory) factory;
            }
        }
    }
}