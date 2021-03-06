﻿using System;
using System.Collections.Generic;
using System.Linq;

using Android.App;
using Android.Content;
using Android.Database;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Support.Design.Widget;
using Android.Support.V4.Widget;
using Android.Support.V4.View;
using Android.Support.V7.App;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Widget;

using Toolbar = Android.Support.V7.Widget.Toolbar;
using System.Threading;
using System.Threading.Tasks;
using Android.Utilities;
using Android.Graphics.Drawables;

namespace TramUrWay.Android
{
    [Activity(Theme = "@style/AppTheme.NoActionBar")]
    public class StopActivity : BaseActivity
    {
        private Line line;
        private Stop stop;
        private TimeStep[] timeSteps;

        private CancellationTokenSource refreshCancellationTokenSource = new CancellationTokenSource();

        private Snackbar snackbar;
        private SwipeRefreshLayout swipeRefresh;
        private RecyclerView listStopList;
        private TextView lineLabel;
        private RecyclerView otherStopList;
        private TextView otherLabel;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            OnCreate(savedInstanceState, Resource.Layout.StopActivity);

            SupportActionBar.SetDisplayHomeAsUpEnabled(true);

            // Handle bundle parameter
            Bundle extras = Intent.Extras;
            if (extras != null && extras.ContainsKey("Stop"))
            {
                int stopId = extras.GetInt("Stop");
                stop = TramUrWayApplication.GetStop(stopId);
            }
#if DEBUG
            else
                stop = TramUrWayApplication.Lines.SelectMany(l => l.Stops).FirstOrDefault(s => s.Name == "Saint-Lazare");
#endif
            if (stop == null)
                throw new Exception("Could not find any stop matching the specified id");

            if (extras != null && extras.ContainsKey("Line"))
            {
                int lineId = extras.GetInt("Line");
                line = TramUrWayApplication.GetLine(lineId);
            }
            else
                line = stop.Line;

            Title = stop.Name;

            // Change toolbar color
            Color color = Utils.GetColorForLine(this, line);
            Color darkColor = new Color(color.R * 2 / 3, color.G * 2 / 3, color.B * 2 / 3);

            SupportActionBar.SetBackgroundDrawable(new ColorDrawable(color));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                Window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);
                Window.ClearFlags(WindowManagerFlags.TranslucentStatus);
                Window.SetStatusBarColor(darkColor);
            }

            // Refresh widget
            swipeRefresh = FindViewById<SwipeRefreshLayout>(Resource.Id.StopActivity_SwipeRefresh);
            swipeRefresh.Refresh += SwipeRefresh_Refresh;
            swipeRefresh.SetColorSchemeColors(color.ToArgb());

            // Initialize UI
            lineLabel = FindViewById<TextView>(Resource.Id.StopActivity_LineLabel);
            lineLabel.Text = line.Name;
            lineLabel.SetTextColor(darkColor);

            listStopList = FindViewById<RecyclerView>(Resource.Id.StopActivity_LineStopList);
            listStopList.HasFixedSize = true;
            listStopList.NestedScrollingEnabled = false;
            listStopList.SetLayoutManager(new WrapLayoutManager(this));
            listStopList.AddItemDecoration(new DividerItemDecoration(this, LinearLayoutManager.Vertical));

            otherLabel = FindViewById<TextView>(Resource.Id.StopActivity_OtherLabel);
            otherLabel.SetTextColor(darkColor);

            otherStopList = FindViewById<RecyclerView>(Resource.Id.StopActivity_OtherStopList);
            otherStopList.HasFixedSize = true;
            otherStopList.NestedScrollingEnabled = false;
            otherStopList.SetLayoutManager(new WrapLayoutManager(this));
            otherStopList.AddItemDecoration(new DividerItemDecoration(this, LinearLayoutManager.Vertical));
        }
        protected override void OnPause()
        {
            refreshCancellationTokenSource.Cancel();

            base.OnPause();
        }
        protected override void OnResume()
        {
            base.OnResume();

            refreshCancellationTokenSource?.Cancel();
            refreshCancellationTokenSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                CancellationTokenSource cancellationTokenSource = refreshCancellationTokenSource;
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    Refresh();
                    await Task.Delay(TramUrWayApplication.GlobalUpdateDelay * 1000);
                }
            });

            swipeRefresh.Post(() => swipeRefresh.Refreshing = true);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            MenuInflater.Inflate(Resource.Menu.StopMenu, menu);

            for (int i = 0; i < menu.Size(); i++)
            {
                IMenuItem item = menu.GetItem(i);

                if (item.ItemId == Resource.Id.StopMenu_Favorite)
                    item.SetIcon(stop.GetIsFavorite() ? Resource.Drawable.ic_star : Resource.Drawable.ic_star_border);
            }

            return true;
        }
        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case global::Android.Resource.Id.Home:
                    OnBackPressed();
                    break;

                case Resource.Id.StopMenu_Favorite:
                    stop.SetIsFavorite(!stop.GetIsFavorite());
                    item.SetIcon(stop.GetIsFavorite() ? Resource.Drawable.ic_star : Resource.Drawable.ic_star_border);
                    break;

                case Resource.Id.StopMenu_Route:
                    Intent intent = new Intent(this, typeof(RoutesActivity));
                    intent.PutExtra("To", stop.Id);
                    StartActivity(intent);
                    break;
            }

            return base.OnOptionsItemSelected(item);
        }

        private void SwipeRefresh_Refresh(object sender, EventArgs e)
        {
            if (snackbar?.IsShown == false)
                snackbar = null;

            Refresh();
        }
        private void Snackbar_Retry(View v)
        {
            snackbar?.Dismiss();
            snackbar = null;

            Refresh();
        }
        private void Snackbar_Activate(View v)
        {
            snackbar?.Dismiss();
            snackbar = null;

            StartActivity(typeof(SettingsActivity));
        }

        private async void Refresh()
        {
            await Task.Run(() =>
            {
                // Online time steps
                try
                {
                    if (TramUrWayApplication.Config.OfflineMode)
                        throw new Exception();

                    swipeRefresh.Post(() => swipeRefresh.Refreshing = true);

                    timeSteps = TramUrWayApplication.Lines.Where(l => l.Stops.Any(s => s.Name == stop.Name))
                                                          .SelectMany(l => TramUrWayApplication.Service.GetLiveTimeSteps(l))
                                                          .Where(t => t.Step.Stop.Name == stop.Name)
                                                          .OrderBy(t => t.Date)
                                                          .ToArray();

                    snackbar?.Dismiss();
                }
                catch (Exception e)
                {
                    DateTime now = DateTime.Now;
                    timeSteps = TramUrWayApplication.Lines.SelectMany(l => l.Routes)
                                         .SelectMany(r => r.Steps.Where(s => s.Stop.Name == stop.Name))
                                         .SelectMany(s => s.Route.TimeTable?.GetStepsFromStep(s, now)?.Take(3) ?? Enumerable.Empty<TimeStep>())
                                         .ToArray();

                    if (timeSteps.Length != 0)
                    {
                        if (snackbar == null)
                        {
                            snackbar = Snackbar.Make(swipeRefresh, "Données hors-ligne", Snackbar.LengthIndefinite);
                            if (TramUrWayApplication.Config.OfflineMode)
                                snackbar = snackbar.SetAction("Activer", Snackbar_Activate);
                            else
                                snackbar = snackbar.SetAction("Réessayer", Snackbar_Retry);


                            snackbar.Show();
                        }
                    }
                    else
                    {
                        timeSteps = null;

                        if (snackbar == null)
                        {
                            snackbar = Snackbar.Make(swipeRefresh, "Aucune donnée disponible", Snackbar.LengthIndefinite);
                            if (TramUrWayApplication.Config.OfflineMode)
                                snackbar = snackbar.SetAction("Activer", Snackbar_Activate);
                            else
                                snackbar = snackbar.SetAction("Réessayer", Snackbar_Retry);


                            snackbar.Show();
                        }
                    }
                }

                swipeRefresh.Post(() => swipeRefresh.Refreshing = false);

                RunOnUiThread(OnRefreshed);
            });
        }
        private void OnRefreshed()
        {
            swipeRefresh.Post(() => swipeRefresh.Refreshing = false);

            if (stop != null && timeSteps != null)
            {
                TimeStep[] lineSteps = timeSteps.Where(s => s.Step.Stop.Line == line)
                                                .OrderBy(s => s.Step.Route.Id)
                                                .ThenBy(s => s.Date)
                                                .ToArray();
                listStopList.SetAdapter(new TimeStepsAdapter(lineSteps));

                TimeStep[] otherSteps = timeSteps.Where(s => s.Step.Stop.Line != line)
                                                 .OrderBy(s => s.Date)
                                                 .ToArray();
                otherStopList.SetAdapter(new TimeStepsAdapter(otherSteps));

                otherLabel.Visibility = otherSteps.Length == 0 ? ViewStates.Gone : ViewStates.Visible;
            }

            swipeRefresh.PostDelayed(() => swipeRefresh.Refreshing = false, 1000);
        }
    }
}