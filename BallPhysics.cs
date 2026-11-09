using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopBounce
{
    internal sealed class BallPhysics
    {
        private const float Gravity = 1500f;
        private const float RestSpeed = 68f;
        private const float MaximumStrikeSpeed = 2300f;
        private const float MinimumCursorSpeed = 35f;
        private const float MaximumStep = 1f / 240f;
        private float radius;
        private PointF position;
        private PointF velocity;
        private Point lastCursor;
        private bool hasCursorSample;
        private bool cursorLatched;

        public BallPhysics(float radius)
        {
            this.radius = Math.Max(1f, radius);
        }

        public float Radius
        {
            get { return radius; }
            set
            {
                float newRadius = Math.Max(1f, value);
                position.Y += radius - newRadius;
                radius = newRadius;
            }
        }

        public PointF Position { get { return position; } }
        public PointF Velocity { get { return velocity; } }
        public PointF Impact { get; private set; }

        public void Reset(Rectangle bounds, Point? near = null)
        {
            if (near.HasValue)
            {
                Point cursor = near.Value;
                float towardCenter = cursor.X < bounds.Left + bounds.Width / 2f ? 1f : -1f;
                float offsetY = cursor.Y - bounds.Top >= radius * 4 ? -radius * 2.8f : radius * 2.8f;
                float insetX = Math.Min(radius, bounds.Width / 2f);
                float insetY = Math.Min(radius, bounds.Height / 2f);
                position = new PointF(
                    Math.Max(bounds.Left + insetX, Math.Min(bounds.Right - insetX, cursor.X + towardCenter * radius * 3)),
                    Math.Max(bounds.Top + insetY, Math.Min(bounds.Bottom - insetY, cursor.Y + offsetY)));
                velocity = new PointF(towardCenter * 170f, -65f);
            }
            else
            {
                position = new PointF(bounds.Left + bounds.Width * 0.35f, bounds.Top + bounds.Height * 0.22f);
                velocity = new PointF(300f, -100f);
            }
            Impact = PointF.Empty;
            ForgetCursor();
        }

        public void ForgetCursor()
        {
            hasCursorSample = false;
            cursorLatched = false;
        }

        public void Step(float seconds, Rectangle bounds, IList<Rectangle> platforms, Point cursor)
        {
            Impact = PointF.Empty;
            if (seconds <= 0f)
                return;

            PointF pointer = hasCursorSample ? lastCursor : cursor;
            lastCursor = cursor;
            hasCursorSample = true;

            float cursorX = cursor.X - pointer.X;
            float cursorY = cursor.Y - pointer.Y;
            float cursorDistance = Length(cursorX, cursorY);
            float cursorSpeed = cursorDistance / seconds;
            PointF direction = cursorDistance > 0f
                ? new PointF(cursorX / cursorDistance, cursorY / cursorDistance)
                : PointF.Empty;

            // Short slices keep collision positions stable when the overlay misses a frame.
            float duration = Math.Min(seconds, 0.075f);
            int slices = (int)Math.Ceiling(duration / MaximumStep);
            float step = duration / slices;
            PointF pointerDelta = new PointF(cursorX / slices, cursorY / slices);

            for (int slice = 0; slice < slices; slice++)
            {
                PointF previousPosition = position;
                Advance(step, bounds, platforms);
                PointF nextPointer = new PointF(pointer.X + pointerDelta.X, pointer.Y + pointerDelta.Y);
                Strike(previousPosition, pointer, nextPointer, direction, cursorSpeed);
                pointer = nextPointer;
            }
        }

        private void Advance(float seconds, Rectangle bounds, IList<Rectangle> platforms)
        {
            float left = bounds.Left + radius;
            float right = bounds.Right - radius;
            float top = bounds.Top + radius;
            float floor = bounds.Bottom - radius;
            float nextX = position.X + velocity.X * seconds;
            velocity.Y += Gravity * seconds;
            float nextY = position.Y + velocity.Y * seconds;

            if (left >= right)
            {
                nextX = bounds.Left + bounds.Width * 0.5f;
                velocity.X = 0f;
            }
            else if (nextX <= left)
            {
                nextX = left;
                if (velocity.X < 0f)
                {
                    RecordImpact(-velocity.X, 0f);
                    velocity.X *= -0.9f;
                }
            }
            else if (nextX >= right)
            {
                nextX = right;
                if (velocity.X > 0f)
                {
                    RecordImpact(-velocity.X, 0f);
                    velocity.X *= -0.9f;
                }
            }

            bool grounded = false;
            if (top >= floor)
            {
                nextY = bounds.Top + bounds.Height * 0.5f;
                velocity.Y = 0f;
            }
            else if (nextY <= top)
            {
                nextY = top;
                if (velocity.Y < 0f)
                {
                    RecordImpact(0f, -velocity.Y);
                    velocity.Y *= -0.83f;
                }
            }
            else
            {
                float landingY = velocity.Y >= 0f
                    ? FindLanding(nextX, nextY, floor, platforms)
                    : floor;
                if (nextY >= landingY)
                {
                    nextY = landingY;
                    if (velocity.Y > RestSpeed)
                    {
                        RecordImpact(0f, -velocity.Y);
                        velocity.Y *= -0.82f;
                    }
                    else if (velocity.Y >= 0f)
                    {
                        velocity.Y = 0f;
                        grounded = true;
                    }
                }
            }

            velocity.X *= 1f - (grounded ? 4.4f : 0.08f) * seconds;
            if (grounded && Math.Abs(velocity.X) < 2f)
                velocity.X = 0f;
            position = new PointF(nextX, nextY);
        }

        private float FindLanding(float nextX, float nextY, float floor, IList<Rectangle> platforms)
        {
            float landingY = floor;
            float travelY = nextY - position.Y;
            float rim = radius * 0.55f;
            for (int index = 0; index < platforms.Count; index++)
            {
                Rectangle platform = platforms[index];
                float centerAtTop = platform.Top - radius;
                if (centerAtTop < position.Y || centerAtTop > nextY || centerAtTop >= landingY)
                    continue;

                float fraction = travelY > 0f ? (centerAtTop - position.Y) / travelY : 0f;
                float xAtTop = position.X + (nextX - position.X) * fraction;
                if (xAtTop + rim > platform.Left && xAtTop - rim < platform.Right)
                    landingY = centerAtTop;
            }
            return landingY;
        }

        private void Strike(PointF previousPosition, PointF pointer, PointF nextPointer, PointF direction, float speed)
        {
            // Sweep the cursor relative to the ball, so a quick swipe can cross it between samples.
            float fromX = pointer.X - previousPosition.X;
            float fromY = pointer.Y - previousPosition.Y;
            float toX = nextPointer.X - position.X;
            float toY = nextPointer.Y - position.Y;
            float contactRadius = radius + 2f;
            if (cursorLatched)
            {
                float releaseRadius = contactRadius + 5f;
                cursorLatched = SquaredLength(toX, toY) <= releaseRadius * releaseRadius;
                return;
            }

            // A cursor moving away from the ball should never pull it along.
            if (speed < MinimumCursorSpeed || fromX * direction.X + fromY * direction.Y > 0.5f)
                return;

            float deltaX = toX - fromX;
            float deltaY = toY - fromY;
            float squaredTravel = SquaredLength(deltaX, deltaY);
            float fraction = squaredTravel > 0f
                ? -(fromX * deltaX + fromY * deltaY) / squaredTravel
                : 0f;
            fraction = Math.Max(0f, Math.Min(1f, fraction));
            if (SquaredLength(fromX + fraction * deltaX, fromY + fraction * deltaY) > contactRadius * contactRadius)
                return;

            float launchSpeed = Math.Min(MaximumStrikeSpeed, speed * 0.93f);
            velocity = new PointF(direction.X * launchSpeed, direction.Y * launchSpeed);
            RecordImpact(velocity.X, velocity.Y);
            cursorLatched = true;
        }

        private void RecordImpact(float x, float y)
        {
            if (SquaredLength(x, y) > Math.Max(1600f, SquaredLength(Impact.X, Impact.Y)))
                Impact = new PointF(x, y);
        }

        private static float Length(float x, float y)
        {
            return (float)Math.Sqrt(SquaredLength(x, y));
        }

        private static float SquaredLength(float x, float y)
        {
            return x * x + y * y;
        }
    }
}
