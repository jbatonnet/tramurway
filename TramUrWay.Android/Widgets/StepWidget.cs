using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Runtime;
using Android.Support.V4.Graphics.Drawable;
using Android.Views;
using Android.Widget;

namespace TramUrWay.Android
{
    [BroadcastReceiver(Label = "Station", Exported = true)]
    [IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
    [MetaData("android.appwidget.provider", Resource = "@xml/stepwidget")]
    public class StepWidget : AppWidgetProvider
    {
        private static ComponentName componentName;
        public static ComponentName GetComponentName(Context context)
        {
            var javaClass = Java.Lang.Class.FromType(typeof(StepWidget));
            return componentName ?? (componentName = new ComponentName(context, javaClass.CanonicalName));
        }

        public override void OnEnabled(Context context)
        {
            if (TramUrWayApplication.Config.EnableWidgetRefresh)
                WidgetUpdateService.Start(context, true);
        }
        public override void OnDisabled(Context context)
        {
            ComponentName widgetComponent = GetComponentName(context);

            AppWidgetManager appWidgetManager = AppWidgetManager.GetInstance(context);
            int[] appWidgetIds = appWidgetManager.GetAppWidgetIds(widgetComponent);

            if (appWidgetIds.Length == 0 && TramUrWayApplication.Config.EnableWidgetRefresh)
                WidgetUpdateService.Stop(context);
        }
        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            Update(context, appWidgetManager, appWidgetIds);
        }
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent.Action == AppWidgetManager.ActionAppwidgetUpdate)
            {
                AppWidgetManager appWidgetManager = AppWidgetManager.GetInstance(context);
                int[] appWidgetIds = intent.GetIntArrayExtra(AppWidgetManager.ExtraAppwidgetIds);

                // Service update
                if (appWidgetIds == null)
                {
                    WidgetUpdateService.Start(context);
                    Update(context, appWidgetManager);
                }

                // System update
                else
                    Update(context, appWidgetManager, appWidgetIds);
            }
            else
                base.OnReceive(context, intent);
        }

        public static void Update(Context context, AppWidgetManager appWidgetManager)
        {
            ComponentName widgetComponent = GetComponentName(context);
            int[] appWidgetIds = appWidgetManager.GetAppWidgetIds(widgetComponent);

            Update(context, appWidgetManager, appWidgetIds);
        }
        public static void Update(Context context, AppWidgetManager appWidgetManager, params int[] appWidgetIds)
        {
            foreach (int appWidgetId in appWidgetIds)
            {
                Step step;
                if (!TramUrWayApplication.Config.StepWidgets.TryGetValue(appWidgetId, out step))
                    return;

                RemoteViews remoteViews = new RemoteViews(context.PackageName, Resource.Layout.StepWidget);

                Intent intent = new Intent(context, typeof(StopActivity));
                intent.PutExtra("Stop", step.Stop.Id);
                intent.PutExtra("Route", step.Route.Id);

                int code = Util.Hash(step.Stop.Id, step.Route.Id);

                PendingIntent pendingIntent = PendingIntent.GetActivity(context, code, intent, 0);
                remoteViews.SetOnClickPendingIntent(Resource.Id.StepWidget_Button, pendingIntent);

                // Update widget UI
                remoteViews.SetTextViewText(Resource.Id.StepWidget_Name, step.Stop.Name);
                remoteViews.SetViewVisibility(Resource.Id.StepWidget_Description, TramUrWayApplication.Config.EnableWidgetRefresh ? ViewStates.Visible : ViewStates.Gone);

                Bitmap bitmap = Utils.GetTransportIconForLine(context, step.Route.Line, 48);
                remoteViews.SetImageViewBitmap(Resource.Id.StepWidget_Icon, bitmap);

                // Get step information
                if (TramUrWayApplication.Config.EnableWidgetRefresh)
                {
                    DateTime now = DateTime.Now;
                    TimeStep[] timeSteps = null;

                    try
                    {
                        timeSteps = TramUrWayApplication.Service.GetLiveTimeSteps(step.Route.Line).Where(t => t.Step.Stop == step.Stop).OrderBy(t => t.Date).Take(2).ToArray();
                    }
                    catch (Exception e)
                    {
                        timeSteps = TramUrWayApplication.Lines.SelectMany(l => l.Routes)
                                             .SelectMany(r => r.Steps.Where(s => s.Stop.Name == step.Stop.Name))
                                             .SelectMany(s => s.Route.TimeTable?.GetStepsFromStep(s, now)?.Take(3) ?? Enumerable.Empty<TimeStep>())
                                             .Take(2)
                                             .ToArray();
                    }

                    remoteViews.SetTextViewText(Resource.Id.StepWidget_Description, timeSteps == null ? "???" : Utils.GetReadableTimes(timeSteps, now, false));
                }

                try
                {
                    appWidgetManager.UpdateAppWidget(appWidgetId, remoteViews);
                }
                catch (Exception e)
                {
                    Toast.MakeText(context, "Exception while updating widget: " + e, ToastLength.Long).Show();
                }
            }
        }
    }
}