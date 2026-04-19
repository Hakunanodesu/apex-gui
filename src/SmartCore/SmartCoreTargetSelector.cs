internal sealed class SmartCoreTargetSelector
{
    public bool TrySelectTarget(in SmartCoreAimAssistContext context, out OnnxDebugBox box)
    {
        box = default;
        if (context.Boxes is null || context.Boxes.Length == 0)
        {
            return false;
        }

        var inputWidth = context.Boxes[0].InputWidth;
        var inputHeight = context.Boxes[0].InputHeight;
        if (inputWidth <= 0 || inputHeight <= 0)
        {
            return false;
        }

        var centerX = inputWidth * 0.5f;
        var centerY = inputHeight * 0.5f;
        var bestIndex = -1;
        var bestDistanceSquared = float.MaxValue;
        for (var i = 0; i < context.Boxes.Length; i++)
        {
            var candidate = context.Boxes[i];
            var dx = candidate.X - centerX;
            var dy = candidate.Y - centerY;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        box = context.Boxes[bestIndex];
        return true;
    }
}
