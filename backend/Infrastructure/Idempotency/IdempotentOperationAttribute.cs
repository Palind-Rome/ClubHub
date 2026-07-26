namespace ClubHub.Api.Infrastructure.Idempotency;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentOperationAttribute : Attribute
{
    public IdempotentOperationAttribute(string operationId) => OperationId = operationId;

    public string OperationId { get; }
}
