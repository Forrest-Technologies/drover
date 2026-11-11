global using System;
global using System.IO;
global using System.Linq;
global using System.Runtime.Versioning;
global using System.Security.Cryptography;
global using System.Threading;
global using System.Threading.Tasks;
global using AwesomeAssertions;
global using Drover.Adapters.Conformance.Secrets;
global using Drover.Adapters.DataProtection;
global using Drover.Data;
global using Drover.Data.Bootstrap;
global using Drover.Data.Identity;
global using Drover.Data.Tenancy;
global using Drover.Domain.Secrets;
global using Drover.Domain.Spaces;
global using Drover.Tests.Common;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging.Abstractions;
global using Npgsql;
global using Testcontainers.PostgreSql;
global using Xunit;

[assembly: AssemblyFixture(typeof(PostgresServer))]
