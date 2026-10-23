namespace PaintQueueLocalAi;

internal static class RequestValidator
{
    public static void Validate(DispatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new ArgumentException("Request id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SharedResourceId))
        {
            throw new ArgumentException("Shared resource id is required.", nameof(request));
        }

        if (request.Queues is null)
        {
            throw new ArgumentException("Queues are required.", nameof(request));
        }

        if (request.Exits is null)
        {
            throw new ArgumentException("Exits are required.", nameof(request));
        }

        if (request.Safety is null)
        {
            throw new ArgumentException("Safety interlocks are required.", nameof(request));
        }

        var seenVehicles = new HashSet<string>(StringComparer.Ordinal);
        var seenQueues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var queue in request.Queues)
        {
            if (queue is null)
            {
                throw new ArgumentException("Queue entry is null.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(queue.Id))
            {
                throw new ArgumentException("Queue id is required.", nameof(request));
            }

            if (!seenQueues.Add(queue.Id))
            {
                throw new ArgumentException($"Duplicate queue id '{queue.Id}'.", nameof(request));
            }

            if (queue.Capacity < 0)
            {
                throw new ArgumentException($"Queue '{queue.Id}' has a negative capacity.", nameof(request));
            }

            if (queue.Vehicles is null)
            {
                throw new ArgumentException($"Queue '{queue.Id}' has no vehicle list.", nameof(request));
            }

            for (var i = 0; i < queue.Vehicles.Count; i++)
            {
                var vehicle = queue.Vehicles[i] ?? throw new ArgumentException($"Queue '{queue.Id}' contains a null vehicle.", nameof(request));
                RememberVehicle(vehicle, seenVehicles, requestName: nameof(request));
            }
        }

        var seenExits = new HashSet<BoothExitId>();
        foreach (var exit in request.Exits)
        {
            if (exit is null)
            {
                throw new ArgumentException("Exit entry is null.", nameof(request));
            }

            if (!seenExits.Add(exit.Id))
            {
                throw new ArgumentException($"Duplicate exit '{exit.Id}'.", nameof(request));
            }

            if (exit.PresentVehicle is not null)
            {
                RememberVehicle(exit.PresentVehicle, seenVehicles, nameof(request));
            }
        }
    }

    private static void RememberVehicle(Vehicle vehicle, HashSet<string> seen, string requestName)
    {
        if (string.IsNullOrWhiteSpace(vehicle.Id))
        {
            throw new ArgumentException("Vehicle id is required.", requestName);
        }

        if (!seen.Add(vehicle.Id))
        {
            throw new ArgumentException($"Duplicate vehicle id '{vehicle.Id}'.", requestName);
        }
    }
}
