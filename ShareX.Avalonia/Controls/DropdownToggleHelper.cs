using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

namespace ShareX.AvaloniaUI.Controls
{
    public static class DropdownToggleHelper
    {
        public static readonly AttachedProperty<long> LastClosedTimestampProperty =
            AvaloniaProperty.RegisterAttached<Popup, long>("LastClosedTimestamp", typeof(DropdownToggleHelper));

        public static readonly AttachedProperty<long> LastDropDownClosedTimestampProperty =
            AvaloniaProperty.RegisterAttached<ComboBox, long>("LastDropDownClosedTimestamp", typeof(DropdownToggleHelper));

        public static readonly AttachedProperty<long> LastFlyoutClosedTimestampProperty =
            AvaloniaProperty.RegisterAttached<FlyoutBase, long>("LastFlyoutClosedTimestamp", typeof(DropdownToggleHelper));

        static DropdownToggleHelper()
        {
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
            {
                if (e.NewValue is false)
                {
                    popup.SetValue(LastClosedTimestampProperty, Stopwatch.GetTimestamp());
                }
            });

            ComboBox.IsDropDownOpenProperty.Changed.AddClassHandler<ComboBox>((cb, e) =>
            {
                if (e.NewValue is false)
                {
                    cb.SetValue(LastDropDownClosedTimestampProperty, Stopwatch.GetTimestamp());
                }
            });

            ComboBox.PointerPressedEvent.AddClassHandler<ComboBox>((cb, e) =>
            {
                long lastClosed = cb.GetValue(LastDropDownClosedTimestampProperty);
                if (lastClosed > 0 && Stopwatch.GetElapsedTime(lastClosed).TotalMilliseconds < 250)
                {
                    e.Handled = true;
                }
            }, RoutingStrategies.Tunnel);

            FlyoutBase.IsOpenProperty.Changed.AddClassHandler<FlyoutBase>((flyout, e) =>
            {
                if (e.NewValue is false)
                {
                    flyout.SetValue(LastFlyoutClosedTimestampProperty, Stopwatch.GetTimestamp());
                }
            });

            Button.PointerPressedEvent.AddClassHandler<Button>((btn, e) =>
            {
                if (btn.Flyout != null && btn.Flyout.IsOpen)
                {
                    btn.SetValue(FlyoutSuppressionProperty, true);
                }
                else
                {
                    btn.SetValue(FlyoutSuppressionProperty, false);
                }
            }, RoutingStrategies.Tunnel);

            Button.ClickEvent.AddClassHandler<Button>((btn, e) =>
            {
                if (btn.Flyout != null && btn.GetValue(FlyoutSuppressionProperty))
                {
                    btn.SetValue(FlyoutSuppressionProperty, false);
                    btn.Flyout.Hide();
                    e.Handled = true;
                }
            });
        }

        public static readonly AttachedProperty<bool> FlyoutSuppressionProperty =
            AvaloniaProperty.RegisterAttached<Button, bool>("FlyoutSuppression", typeof(DropdownToggleHelper));

        public static void Toggle(Popup? popup, int suppressionWindowMs = 250)
        {
            if (popup == null) return;

            if (popup.IsOpen)
            {
                popup.IsOpen = false;
            }
            else
            {
                long lastClosed = popup.GetValue(LastClosedTimestampProperty);
                if (lastClosed == 0 || Stopwatch.GetElapsedTime(lastClosed).TotalMilliseconds >= suppressionWindowMs)
                {
                    popup.IsOpen = true;
                }
            }
        }

        public static void Toggle(FlyoutBase? flyout, Control target, int suppressionWindowMs = 250)
        {
            if (flyout == null) return;

            if (flyout.IsOpen)
            {
                flyout.Hide();
            }
            else
            {
                long lastClosed = flyout.GetValue(LastFlyoutClosedTimestampProperty);
                if (lastClosed == 0 || Stopwatch.GetElapsedTime(lastClosed).TotalMilliseconds >= suppressionWindowMs)
                {
                    flyout.ShowAt(target);
                }
            }
        }
    }
}
