using System;
using System.Collections.ObjectModel;
using System.Linq;
using SmartHunter.Core.Data;
using SmartHunter.Game.Data.ViewModels;
using SmartHunter.Game.Helpers;

namespace SmartHunter.Game.Data.WidgetContexts
{
    public class TeamWidgetContext : WidgetContext
    {
        public Collection<Player> Players { get; private set; }
        public ObservableCollection<Player> Fake_Players { get; private set; } // TODO: Can dis be done inside the xaml file?

        bool m_DontShowIfAlone = false;
        public bool DontShowIfAlone
        {
            get { return m_DontShowIfAlone; }
            set { SetProperty(ref m_DontShowIfAlone, value); }
        }

        bool m_ShowBars = true;
        public bool ShowBars
        {
            get { return m_ShowBars; }
            set { SetProperty(ref m_ShowBars, value); }
        }

        bool m_ShowNumbers = true;
        public bool ShowNumbers
        {
            get { return m_ShowNumbers; }
            set { SetProperty(ref m_ShowNumbers, value); }
        }

        bool m_ShowPercents = false;
        public bool ShowPercents
        {
            get { return m_ShowPercents; }
            set { SetProperty(ref m_ShowPercents, value); }
        }

        bool m_ShowChart = false;
        public bool ShowChart
        {
            get { return m_ShowChart; }
            set { SetProperty(ref m_ShowChart, value); }
        }

        public TeamWidgetContext()
        {
            Players = new Collection<Player>();
            Fake_Players = new ObservableCollection<Player>();

            UpdateFromConfig();
        }

        public event EventHandler PlayersDamageUpdated;

        public Player UpdateAndGetPlayer(int index, string name, int damage)
        {
            if (String.IsNullOrEmpty(name) && damage == 0)
            {
                if (index < Players.Count)
                {
                    Players.RemoveAt(index);
                    if (DontShowIfAlone && Players.Count() <= 1)
                    {
                        Fake_Players.Clear();
                    }
                    else
                    {
                        Fake_Players.RemoveAt(index);
                    }
                }
                return null;
            }

            while (index >= Players.Count)
            {
                Players.Add(new Player() { Index = Players.Count, Name = LocalizationHelper.GetString(LocalizationHelper.UnknownPlayerStringId) });

                if (DontShowIfAlone && Players.Count() <= 1)
                {
                    Fake_Players.Clear();
                }
                else
                {
                    Fake_Players.Add(Players[Players.Count() - 1]);
                }
            }

            Player player = Players[index];
            if (!String.IsNullOrEmpty(name))
            {
                player.Name = name;
            }
            else if (String.IsNullOrEmpty(player.Name))
            {
                player.Name = LocalizationHelper.GetString(LocalizationHelper.UnknownPlayerStringId);
            }

            if (!OverlayViewModel.Instance.DebugWidget.Context.CurrentGame.IsPlayerInExpedition)
            {
                player.Damage = damage;
            }

            return player;
        }

        public void UpdateFractions()
        {
            var playersWithDamage = Players.Where(player => player.Damage > 0);
            if (!playersWithDamage.Any())
            {
                foreach (var player in Players)
                {
                    player.DamageFraction = 0;
                    player.BarFraction = 0;
                }

                return;
            }

            NormalizeDamagePoints();
            PlayersDamageUpdated?.Invoke(this, EventArgs.Empty);

            var highestDamagePlayers = Players.OrderByDescending(player => player.Damage).Take(1);
            if (highestDamagePlayers.Any())
            {
                int totalDamage = Players.Sum(player => player.Damage);

                var highestDamagePlayer = highestDamagePlayers.First();
                highestDamagePlayer.DamageFraction = (float)highestDamagePlayer.Damage / (float)totalDamage;
                highestDamagePlayer.BarFraction = 1;
                //Log.WriteLine(String.Format("{0} {1} {2}", highestDamagePlayer.Damage.ToString(), highestDamagePlayer.DamageFraction.ToString(), highestDamagePlayer.BarFraction.ToString()));
                foreach (var otherPlayer in Players.Except(highestDamagePlayers))
                {
                    otherPlayer.DamageFraction = (float)otherPlayer.Damage / (float)totalDamage;
                    otherPlayer.BarFraction = (float)otherPlayer.Damage / (float)highestDamagePlayer.Damage;
                }
            }
        }

        public void ClearPlayers()
        {
            Players.Clear();
            Fake_Players.Clear();
            PlayersDamageUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Make sure that all players have datapoints at latest available timestamp
        /// </summary>
        private void NormalizeDamagePoints()
        {
            var maxTimeStamp = Players.Max(p => p.DamagePoints.LastOrDefault()?.TimeStamp);
            if (maxTimeStamp == null) return;

            foreach (var player in Players)
            {
                var last = player.DamagePoints.LastOrDefault();
                if (last == null)
                {
                    player.DamagePoints.Add(new DamagePoint(maxTimeStamp.Value, 0));
                }
                else if (last.TimeStamp < maxTimeStamp)
                {
                    player.DamagePoints.Add(new DamagePoint(maxTimeStamp.Value, last.Damage));
                }
            }
        }

        /// <summary>
        /// Adds datapoints for current timestamp so plot will continue even without new damage
        /// </summary>
        /// <param name="msThreshold">add point only if latest point older then this value</param>
        public void PadDamagePoints(long msThreshold = 0)
        {
            var maxTimeStamp = Players.Max(p => p.DamagePoints.LastOrDefault()?.TimeStamp);
            if (maxTimeStamp == null)
            {
                // no datapoints present, don't have to update anything
                return;
            }

            var now = DateTime.Now.ToFileTime();
            if (msThreshold != 0 && now - maxTimeStamp <= msThreshold)
            {
                return;
            }

            foreach (var player in Players)
            {
                var dmgPoints = player.DamagePoints;
                var last = dmgPoints.LastOrDefault();
                var beforeLast = dmgPoints.Count > 1
                    ? dmgPoints[dmgPoints.Count - 2]
                    : null;

                if (last == null)
                {
                    // no point present, adding first one at 0 damage
                    dmgPoints.Add(new DamagePoint(now, 0));
                }
                else if (beforeLast?.Damage == last.Damage)
                {
                    // if last two points identical, we can just update last one
                    // Removing and adding again to trigger collection update
                    dmgPoints.RemoveAt(dmgPoints.Count - 1);
                    last.TimeStamp = now;
                    dmgPoints.Add(last);
                }
                else
                {
                    dmgPoints.Add(new DamagePoint(now, last.Damage));
                }
            }
        }

        public override void UpdateFromConfig()
        {
            base.UpdateFromConfig();

            DontShowIfAlone = ConfigHelper.Main.Values.Overlay.TeamWidget.DontShowIfAlone;
            ShowBars = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowBars;
            ShowNumbers = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowNumbers;
            ShowPercents = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowPercents;
            ShowChart = ConfigHelper.Main.Values.Overlay.TeamWidget.ShowChart;
        }
    }
}
