namespace Localll.StoreOrders.API.Features;

public record OrderItemRequest(Guid ProductId, int Quantity);

public record CreateOrderRequest(
    Guid StoreId,
    string DeliveryAddress,
    string CustomerName,
    string CustomerPhone,
    string PaymentMethod,                  // "Upi" | "Cod"
    List<OrderItemRequest> Items);

/// <summary>Returned after creating a UPI order — the merchant details to pay + place-order token.</summary>
public record UpiPaymentInfo(
    Guid OrderId,
    string MerchantUpiId,
    string StoreName,
    decimal Amount,
    string UpiDeepLink);                   // upi://pay?... for the QR

public record AttachScreenshotRequest(string ScreenshotUrl);
public record ReviewPaymentRequest(bool Approved, string? RejectionReason);
public record WithdrawRequest(decimal Amount);
