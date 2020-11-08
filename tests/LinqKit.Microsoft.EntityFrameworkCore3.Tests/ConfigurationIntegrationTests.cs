using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using LinqKit.EntityFramework.Tests;
using LinqKit.Microsoft.EntityFrameworkCore3;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace LinqKit.Microsoft.EntityFrameworkCore.Tests
{
    public class ConfigurationIntegrationTests : IDisposable
    {
        private TestContext _db;

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
            _db = null;
        }

        public void Init()
        {
            _db.Database.EnsureCreated();

            _db.Entities.RemoveRange(_db.Entities.ToList());
            _db.Entities.AddRange(new Entity {Value = 123}, new Entity {Value = 67}, new Entity {Value = 3});
            _db.SaveChanges();
        }

        [Fact]
        public async Task WithDefaultQueryTranslationPreprocessorFactory()
        {
            var builder = new DbContextOptionsBuilder();
            builder.UseSqlite($"Filename=LinqKit.{Guid.NewGuid()}.db");
            builder.UseLinqKit();
            _db = new TestContext(builder.Options);

            Init();
            var eParam = Expression.Parameter(typeof(Entity), "e");
            var eProp = Expression.PropertyOrField(eParam, "Value");

            var conditions =
                (from item in new List<int> {10, 20, 30, 80}
                    select Expression.LessThan(eProp, Expression.Constant(item))).Aggregate(Expression.OrElse);

            var combined = Expression.Lambda<Func<Entity, bool>>(conditions, eParam);

            var q = from e in _db.Entities where combined.Invoke(e) select new {e.Value};

            var res = await q.ToListAsync().ConfigureAwait(false);

            Assert.Equal(2, res.Count);
            Assert.Equal(67, res.First().Value);
        }

        [Fact]
        public async Task WithCustomQueryTranslationPreprocessorFactory()
        {
            try
            {
                var builder = new DbContextOptionsBuilder();
                builder.UseSqlite($"Filename=LinqKit.{Guid.NewGuid()}.db");
                builder.ReplaceService<IQueryTranslationPreprocessorFactory, TestQueryTranslationPreprocessorFactory>();
                builder.UseLinqKit();
                _db = new TestContext(builder.Options);

                Init();
                var eParam = Expression.Parameter(typeof(Entity), "e");
                var eProp = Expression.PropertyOrField(eParam, "Value");

                var conditions =
                    (from item in new List<int> {10, 20, 30, 80}
                        select Expression.LessThan(eProp, Expression.Constant(item))).Aggregate(Expression.OrElse);

                var combined = Expression.Lambda<Func<Entity, bool>>(conditions, eParam);

                var q = from e in _db.Entities where combined.Invoke(e) select new {e.Value};

                var res = await q.ToListAsync().ConfigureAwait(false);

                Assert.Equal(2, res.Count);
                Assert.Equal(67, res.First().Value);
                Assert.True(TestQueryTranslationPreprocessor.Executed);
            }
            finally
            {
                TestQueryTranslationPreprocessor.Executed = false;
            }
        }

        private class TestQueryTranslationPreprocessorFactory : IQueryTranslationPreprocessorFactory
        {
            private readonly QueryTranslationPreprocessorDependencies _dependencies;

            public TestQueryTranslationPreprocessorFactory(QueryTranslationPreprocessorDependencies dependencies,
                IServiceProvider serviceProvider)
            {
                _dependencies = dependencies;
            }

            public QueryTranslationPreprocessor Create(QueryCompilationContext queryCompilationContext)
            {
                return new TestQueryTranslationPreprocessor(_dependencies, queryCompilationContext);
            }
        }
    }

    public class TestQueryTranslationPreprocessor : QueryTranslationPreprocessor
    {
        public TestQueryTranslationPreprocessor(QueryTranslationPreprocessorDependencies dependencies,
            QueryCompilationContext queryCompilationContext) : base(dependencies, queryCompilationContext)
        {
        }

        public static bool Executed { get; set; }

        public override Expression Process(Expression query)
        {
            Executed = true;
            return base.Process(query);
        }
    }
}