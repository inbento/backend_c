namespace Models.Dto.V1
{
    public class V1UpdateOrdersStatusRequest
    {
        public long[] OrderIds { get; set; }

        public string NewStatus { get; set; }
    }
}