using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Features.Customers;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct);

    Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Lists customers; <paramref name="type"/> and <paramref name="status"/> are enum names (400 when unknown).</summary>
    Task<CustomerListResponse> ListAsync(PageQuery page, string? type, string? status, CancellationToken ct);

    Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct);

    Task<CustomerResponse> ArchiveAsync(Guid id, CancellationToken ct);

    Task<CustomerResponse> ReactivateAsync(Guid id, CancellationToken ct);
}

public sealed class CustomerService(AppDbContext db, TimeProvider timeProvider) : ICustomerService
{
    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var details = request.ToDetails();
        await EnsureUniqueAsync(null, details.Email, request.UserId, ct);

        var customer = Customer.Create(details, request.UserId, timeProvider.GetUtcNow());
        db.Set<Customer>().Add(customer);
        await SaveAsync(ct);
        return CustomerResponse.FromEntity(customer);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer", id);
        return CustomerResponse.FromEntity(customer);
    }

    private static readonly Dictionary<string, Expression<Func<Customer, object>>> SortMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = c => c.SortName,
        ["email"] = c => c.Email,
        ["createdAt"] = c => c.CreatedAt,
    };

    public async Task<CustomerListResponse> ListAsync(PageQuery page, string? type, string? status, CancellationToken ct)
    {
        var typeFilter = EnumFilter.Parse<CustomerType>(type, "type");
        var statusFilter = EnumFilter.Parse<CustomerStatus>(status, "status");

        var query = db.Set<Customer>().AsNoTracking();
        if (page.SearchTerm is { } term)
        {
            query = query.Where(CustomerSearch.Matches(SqlLike.Contains(term)));
        }

        if (typeFilter is { } t)
        {
            query = query.Where(c => c.Type == t);
        }

        // Segment counts ignore the status filter, so the segments do not change when one is selected.
        var sorted = query.ApplySort(page, SortMap, c => c.Id, "name");
        var statusCounts = await query.CountByAsync(c => c.Status, ct);

        if (statusFilter is { } s)
        {
            sorted = sorted.Where(c => c.Status == s);
        }

        var customers = await sorted.ToPagedAsync(page, ct);
        var summaries = await LoadSummariesAsync(customers.Items.Select(c => c.Id).ToList(), ct);
        var items = customers.Items.Select(c =>
        {
            var (supplyPoints, lastActivity) = summaries.GetValueOrDefault(c.Id);
            return CustomerListItemResponse.From(c, supplyPoints, lastActivity is { } l && l > c.UpdatedAt ? l : c.UpdatedAt);
        }).ToList();

        return new CustomerListResponse(items, customers.Page, customers.PageSize, customers.TotalCount, statusCounts);
    }

    /// <summary>Supply point count and newest quote/installation/ticket update for a page of customers, in two grouped queries.</summary>
    private async Task<Dictionary<Guid, (int SupplyPoints, DateTimeOffset? LastActivity)>> LoadSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, (int SupplyPoints, DateTimeOffset? LastActivity)>();
        if (ids.Count == 0)
        {
            return result;
        }

        var supplyPoints = await db.Set<SupplyPoint>().AsNoTracking()
            .Where(x => ids.Contains(x.CustomerId))
            .GroupBy(x => x.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var activity = await db.Set<Quote>().AsNoTracking().Where(x => ids.Contains(x.CustomerId)).Select(x => new { x.CustomerId, x.UpdatedAt })
            .Concat(db.Set<Installation>().AsNoTracking().Where(x => ids.Contains(x.CustomerId)).Select(x => new { x.CustomerId, x.UpdatedAt }))
            .Concat(db.Set<ServiceTicket>().AsNoTracking().Where(x => ids.Contains(x.CustomerId)).Select(x => new { x.CustomerId, x.UpdatedAt }))
            .GroupBy(x => x.CustomerId)
            .Select(g => new { CustomerId = g.Key, Last = (DateTimeOffset?)g.Max(x => x.UpdatedAt) })
            .ToListAsync(ct);

        foreach (var row in supplyPoints)
        {
            result[row.CustomerId] = (row.Count, null);
        }

        foreach (var row in activity)
        {
            result[row.CustomerId] = (result.GetValueOrDefault(row.CustomerId).SupplyPoints, row.Last);
        }

        return result;
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        var details = request.ToDetails();
        await EnsureUniqueAsync(id, details.Email, request.UserId, ct);

        customer.Update(details, request.UserId, timeProvider.GetUtcNow());
        await SaveAsync(ct);
        return CustomerResponse.FromEntity(customer);
    }

    public async Task<CustomerResponse> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        customer.Archive(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return CustomerResponse.FromEntity(customer);
    }

    public async Task<CustomerResponse> ReactivateAsync(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, ct);
        customer.Reactivate(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return CustomerResponse.FromEntity(customer);
    }

    private async Task<Customer> FindAsync(Guid id, CancellationToken ct) =>
        await db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Customer", id);

    private async Task EnsureUniqueAsync(Guid? customerId, string email, Guid? userId, CancellationToken ct)
    {
        var normalizedEmail = CustomerDetails.NormalizeEmail(email);
        if (await db.Set<Customer>().AnyAsync(c => c.Email == normalizedEmail && c.Id != customerId, ct))
        {
            throw new DomainException($"A customer with e-mail '{normalizedEmail}' already exists.");
        }

        if (userId is { } uid)
        {
            if (!await db.Set<AppUser>().AnyAsync(u => u.Id == uid, ct))
            {
                throw new RequestValidationException(nameof(CustomerRequest.UserId), "User account does not exist.");
            }

            if (await db.Set<Customer>().AnyAsync(c => c.UserId == uid && c.Id != customerId, ct))
            {
                throw new DomainException("The user account is already linked to another customer.");
            }
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request won the race after our pre-check.
            throw new DomainException("A customer with the same e-mail or user account already exists.");
        }
    }
}
