using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Contracts;

public interface IContractService
{
    Task<ContractResponse> CreateAsync(CreateContractRequest request, CancellationToken ct);

    Task<ContractResponse> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ContractResponse>> ListAsync(Guid? customerId, Guid? quoteId, ContractStatus? status, CancellationToken ct);
}

public sealed class ContractService(AppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IContractService
{
    public async Task<ContractResponse> CreateAsync(CreateContractRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var quoteId = request.QuoteId!.Value;

        var quote = await db.Set<Quote>().AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new RequestValidationException(nameof(CreateContractRequest.QuoteId), "Quote does not exist.");
        if (quote.Status != QuoteStatus.Accepted)
        {
            throw new DomainException($"A contract can be created only from an accepted quote (the quote is {quote.Status}).");
        }

        if (await db.Set<Contract>().AnyAsync(c => c.QuoteId == quoteId, ct))
        {
            throw new DomainException("A contract already exists for this quote.");
        }

        var customer = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == quote.CustomerId, ct)
            ?? throw new DomainException("The customer of the quote no longer exists.");
        customer.EnsureActive();

        var lines = quote.Lines
            .OrderBy(l => l.Position)
            .Select(l => new ContractLineData(l.ComponentId, l.ComponentType, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice))
            .ToList();
        var contract = Contract.Create(
            quote.CustomerId,
            quote.Id,
            quote.VatRatePercent,
            lines,
            new QuoteTotals(quote.NetTotal, quote.VatTotal, quote.GrossTotal),
            userId,
            timeProvider.GetUtcNow());

        db.Set<Contract>().Add(contract);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request created the contract of this quote after our pre-check.
            throw new DomainException("A contract already exists for this quote.");
        }

        return ContractResponse.FromEntity(contract);
    }

    public async Task<ContractResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var contract = await db.Set<Contract>().AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contract", id);
        return ContractResponse.FromEntity(contract);
    }

    public async Task<IReadOnlyList<ContractResponse>> ListAsync(Guid? customerId, Guid? quoteId, ContractStatus? status, CancellationToken ct)
    {
        var query = db.Set<Contract>().AsNoTracking().Include(c => c.Lines).AsQueryable();
        if (customerId is { } customer)
        {
            query = query.Where(c => c.CustomerId == customer);
        }

        if (quoteId is { } quote)
        {
            query = query.Where(c => c.QuoteId == quote);
        }

        if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }

        var contracts = await query.OrderByDescending(c => c.Sequence).ToListAsync(ct);
        return contracts.ConvertAll(ContractResponse.FromEntity);
    }
}
