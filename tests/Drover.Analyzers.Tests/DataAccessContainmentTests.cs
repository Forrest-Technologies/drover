namespace Drover.Analyzers.Tests;

/// <summary>DRV0008: data access types stay in Drover.Data, and nothing public there returns IQueryable.</summary>
public sealed class DataAccessContainmentTests
{
    #region Private Fields

    private const string EngineAssembly = "Drover.Engine";

    private const string DataAssembly = "Drover.Data";

    // Stands in for EF Core and for the real context, which the sample compilation does not reference.
    private const string DataStub = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbSet<T> where T : class
            {
            }
        }

        namespace Drover.Data
        {
            public class DroverDbContext
            {
            }
        }

        public class Order
        {
        }

        """;

    #endregion

    #region Non-compliant samples

    [Fact]
    public Task Flags_the_context_as_a_constructor_parameter_outside_Drover_Data() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            public class OrderService(Drover.Data.{|DRV0008:DroverDbContext|} context)
            {
            }

            {{DataStub}}
            """,
            EngineAssembly);

    [Fact]
    public Task Flags_a_DbSet_field_outside_Drover_Data() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using Microsoft.EntityFrameworkCore;

            public class OrderService
            {
                private {|DRV0008:DbSet<Order>|} orders;
            }

            {{DataStub}}
            """,
            EngineAssembly);

    [Fact]
    public Task Flags_IQueryable_outside_Drover_Data_including_through_var() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            public class OrderService
            {
                public int Count(List<Order> orders)
                {
                    {|DRV0008:IQueryable<Order>|} first = orders.AsQueryable();
                    {|DRV0008:var|} second = orders.AsQueryable();
                    return first.Count() + second.Count();
                }
            }

            {{DataStub}}
            """,
            EngineAssembly);

    [Fact]
    public Task Flags_a_public_method_in_Drover_Data_that_returns_IQueryable() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Linq;

            public class OrderRepository
            {
                public IQueryable<Order> {|DRV0008:ListOrders|}() => null;
            }

            {{DataStub}}
            """,
            DataAssembly);

    [Fact]
    public Task Flags_a_public_property_and_a_wrapped_IQueryable_in_Drover_Data() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Linq;
            using System.Threading.Tasks;

            public class OrderRepository
            {
                public IQueryable<Order> {|DRV0008:Orders|} => null;

                public Task<IQueryable<Order>> {|DRV0008:FindOrders|}() => null;
            }

            {{DataStub}}
            """,
            DataAssembly);

    #endregion

    #region Compliant samples

    [Fact]
    public Task Allows_Drover_Data_to_use_the_context_DbSet_and_IQueryable_internally() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public class OrderRepository(Drover.Data.DroverDbContext context)
            {
                private DbSet<Order> orders;

                public List<Order> ListOrders() => Query().ToList();

                internal IQueryable<Order> Query() => orders is null ? null : new List<Order>().AsQueryable();
            }

            {{DataStub}}
            """,
            DataAssembly);

    [Fact]
    public Task Allows_a_test_project_to_use_all_three() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public class OrderTests(Drover.Data.DroverDbContext context)
            {
                private DbSet<Order> orders;

                public IQueryable<Order> Query(List<Order> list) => list.AsQueryable();
            }

            {{DataStub}}
            """,
            "Drover.Data.Tests");

    [Fact]
    public Task Allows_outside_code_that_calls_a_repository_and_uses_plain_collections() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Collections.Generic;
            using System.Linq;

            public class OrderService(OrderRepository orders)
            {
                public int Count()
                {
                    var all = orders.ListOrders();
                    return all.Count;
                }
            }

            public class OrderRepository
            {
                public List<Order> ListOrders() => new();
            }

            {{DataStub}}
            """,
            EngineAssembly);

    [Fact]
    public Task Allows_a_non_public_member_in_Drover_Data_to_return_IQueryable() =>
        Verify.Analyzer<DataAccessContainmentAnalyzer>(
            $$"""
            using System.Linq;

            public class OrderRepository
            {
                private IQueryable<Order> Hidden() => null;

                protected internal IQueryable<Order> Shared() => null;

                internal IQueryable<Order> Inside { get; }
            }

            {{DataStub}}
            """,
            DataAssembly);

    #endregion
}
