using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Customers;

public interface ICustomerService
{
    Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct);

    Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct);

    Task<CustomerListResponse> ListAsync(string? q, int page, int pageSize, CancellationToken ct);

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

    public const int MaxPageSize = 100;

    public async Task<CustomerListResponse> ListAsync(string? q, int page, int pageSize, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["Page must be at least 1."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var query = db.Set<Customer>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{EscapeLike(q.Trim())}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.SortName, pattern, "\\")
                || EF.Functions.ILike(c.FirstName + " " + c.LastName, pattern, "\\")
                || EF.Functions.ILike(c.Email, pattern, "\\")
                || (c.Phone != null && EF.Functions.ILike(c.Phone, pattern, "\\"))
                || (c.Ico != null && EF.Functions.ILike(c.Ico, pattern, "\\")));
        }

        var total = await query.CountAsync(ct);
        var customers = await query
            .OrderBy(c => c.SortName).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new CustomerListResponse(customers.ConvertAll(CustomerResponse.FromEntity), page, pageSize, total);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

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
