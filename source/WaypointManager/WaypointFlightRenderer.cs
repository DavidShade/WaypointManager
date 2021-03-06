﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;
using Contracts;
using FinePrint;
using FinePrint.Utilities;

namespace WaypointManager
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    class WaypointFlightRenderer : MonoBehaviour
    {
        private GUIStyle nameStyle = null;
        private GUIStyle valueStyle = null;
        private GUIStyle hintTextStyle = null;

        private bool visible = true;
        private Waypoint selectedWaypoint = null;
        private string waypointName = "";
        private Rect windowPos;
        private bool newClick = false;
        private AltimeterSliderButtons asb = null;

        private float referencePos = 0.0f;
        private float referenceUISize = 0.0f;
        private float lastPos = 0.0f;
        private bool referenceSet = false;

        private const double MIN_TIME = 300;
        private const double MIN_DISTANCE = 25000;
        private const double MIN_SPEED = MIN_DISTANCE / MIN_TIME;
        private const double FADE_TIME = 20;

        void Start()
        {
            if (MapView.MapCamera.gameObject.GetComponent<WaypointFlightRenderer>() == null)
            {
                MapView.MapCamera.gameObject.AddComponent<WaypointFlightRenderer>();

                // Destroy this object - otherwise we'll have two
                Destroy(this);
            }

            GameEvents.onGameSceneLoadRequested.Add(new EventData<GameScenes>.OnEvent(OnGameSceneLoadRequested));
            GameEvents.onHideUI.Add(new EventVoid.OnEvent(OnHideUI));
            GameEvents.onShowUI.Add(new EventVoid.OnEvent(OnShowUI));
        }

        protected void OnDestroy()
        {
            GameEvents.onGameSceneLoadRequested.Remove(new EventData<GameScenes>.OnEvent(OnGameSceneLoadRequested));
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
        }

        public void OnGameSceneLoadRequested(GameScenes gameScene)
        {
            asb = null;
        }

        public void OnHideUI()
        {
            visible = false;
        }

        public void OnShowUI()
        {
            visible = true;
        }

        public void OnGUI()
        {
            if (visible)
            {
                if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
                {
                    newClick = true;
                }

                if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                {
                    // Draw the marker for custom waypoints that are currently being created
                    CustomWaypointGUI.DrawMarker();

                    // Draw waypoints if not in career mode
                    if (ContractSystem.Instance == null && MapView.MapIsEnabled)
                    {
                        foreach (WaypointData wpd in WaypointData.Waypoints)
                        {
                            if (wpd.celestialBody != null && wpd.waypoint.celestialName == wpd.celestialBody.name)
                            {
                                if (Event.current.type == EventType.Repaint)
                                {
                                    wpd.SetAlpha();
                                    Util.DrawWaypoint(wpd.celestialBody, wpd.waypoint.latitude, wpd.waypoint.longitude,
                                        wpd.waypoint.altitude, wpd.waypoint.id, wpd.waypoint.seed, wpd.currentAlpha);
                                }

                                // Handling clicking on the waypoint
                                if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
                                {
                                    HandleClick(wpd);
                                }

                                // Draw hint text
                                if (Event.current.type == EventType.Repaint)
                                {
                                    HintText(wpd);
                                }
                            }
                        }
                    }
                }

                if (HighLogic.LoadedSceneIsFlight && !MapView.MapIsEnabled)
                {
                    SetupStyles();

                    WaypointData.CacheWaypointData();

                    foreach (WaypointData wpd in WaypointData.Waypoints)
                    {
                        DrawWaypoint(wpd);
                    }
                }

                if (HighLogic.LoadedSceneIsFlight && (!MapView.MapIsEnabled || ContractSystem.Instance == null))
                {
                    ShowNavigationWindow();
                }
            }
        }

        // Styles taken directly from Kerbal Engineer Redux - because they look great and this will
        // make our display consistent with that
        protected void SetupStyles()
        {
            if (nameStyle != null)
            {
                return;
            }

            nameStyle = new GUIStyle(HighLogic.Skin.label)
            {
                normal =
                {
                    textColor = Color.white
                },
                margin = new RectOffset(),
                padding = new RectOffset(5, 0, 0, 0),
                alignment = TextAnchor.MiddleRight,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 20.0f
            };

            valueStyle = new GUIStyle(HighLogic.Skin.label)
            {
                margin = new RectOffset(),
                padding = new RectOffset(0, 5, 0, 0),
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                fixedHeight = 20.0f
            };

            hintTextStyle = new GUIStyle(HighLogic.Skin.box)
            {
                padding = new RectOffset(4, 4, 7, 4),
                font = MapView.OrbitIconsTextSkin.label.font,
                fontSize = MapView.OrbitIconsTextSkin.label.fontSize,
                fontStyle = MapView.OrbitIconsTextSkin.label.fontStyle,
                fixedWidth = 0,
                fixedHeight = 0,
                stretchHeight = true,
                stretchWidth = true
            };
        }

        protected void DrawWaypoint(WaypointData wpd)
        {
            // Not our planet
            CelestialBody celestialBody = FlightGlobals.currentMainBody;
            if (celestialBody == null || wpd.waypoint.celestialName != celestialBody.name)
            {
                return;
            }

            // Check if the waypoint should be visible
            if (!wpd.waypoint.visible)
            {
                return;
            }

            // Figure out waypoint label
            string label = wpd.waypoint.name + (wpd.waypoint.isClustered ? (" " + StringUtilities.IntegerToGreek(wpd.waypoint.index)) : "");

            // Set the alpha and do a nice fade
            wpd.SetAlpha();

            // Decide whether to actually draw the waypoint
            if (FlightGlobals.ActiveVessel != null)
            {
                // Figure out the distance to the waypoint
                Vessel v = FlightGlobals.ActiveVessel;

                // Only change alpha if the waypoint isn't the nav point
                if (!Util.IsNavPoint(wpd.waypoint))
                {
                    // Get the distance to the waypoint at the current speed
                    double speed = v.srfSpeed < MIN_SPEED ? MIN_SPEED : v.srfSpeed;
                    double directTime = Util.GetStraightDistance(wpd) / speed;

                    // More than two minutes away
                    if (directTime > MIN_TIME || Config.waypointDisplay != Config.WaypointDisplay.ALL)
                    {
                        return;
                    }
                    else if (directTime >= MIN_TIME - FADE_TIME)
                    {
                        wpd.currentAlpha = (float)((MIN_TIME - directTime) / FADE_TIME) * Config.opacity;
                    }
                }
                // Draw the distance information to the nav point
                else
                {
                    // Draw the distance to waypoint text
                    if (Event.current.type == EventType.Repaint)
                    {
                        if (asb == null)
                        {
                            asb = UnityEngine.Object.FindObjectOfType<AltimeterSliderButtons>();
                        }

                        if (referenceUISize != ScreenSafeUI.VerticalRatio || !referenceSet)
                        {
                            referencePos = ScreenSafeUI.referenceCam.ViewportToScreenPoint(asb.transform.position).y;
                            referenceUISize = ScreenSafeUI.VerticalRatio;

                            // Need two consistent numbers in a row to set the reference
                            if (lastPos == referencePos)
                            {
                                referenceSet = true;
                            }
                            else
                            {
                                lastPos = referencePos;
                            }
                        }

                        float ybase = (referencePos - ScreenSafeUI.referenceCam.ViewportToScreenPoint(asb.transform.position).y + Screen.height / 11.67f) / ScreenSafeUI.VerticalRatio;

                        string timeToWP = GetTimeToWaypoint(wpd);
                        if (Config.hudDistance)
                        {
                            GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 20f), "Distance to " + label + ":", nameStyle);
                            GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 20f),
                                v.state != Vessel.State.DEAD ? Util.PrintDistance(wpd) : "N/A", valueStyle);
                            ybase += 18f;
                        }

                        if (timeToWP != null && Config.hudTime)
                        {
                            GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 20f), "ETA to " + label + ":", nameStyle);
                            GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 20f),
                                v.state != Vessel.State.DEAD ? timeToWP : "N/A", valueStyle);
                            ybase += 18f;
                        }

                        if (Config.hudHeading)
                        {
                            GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 20f), "Heading to " + label + ":", nameStyle);
                            GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 20f),
                                v.state != Vessel.State.DEAD ? wpd.heading.ToString("N1") : "N/A", valueStyle);
                            ybase += 18f;
                        }

                        if (Config.hudAngle && v.mainBody == wpd.celestialBody)
                        {
                            double distance = Util.GetLateralDistance(wpd);
                            double heightDist = wpd.waypoint.altitude + wpd.waypoint.height - v.altitude;
                            double angle = Math.Atan2(heightDist, distance) * 180.0 / Math.PI;

                            GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 20f), "Angle to " + label + ":", nameStyle);
                            GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 20f),
                                v.state != Vessel.State.DEAD ? angle.ToString("N2") : "N/A", valueStyle);
                            ybase += 18f;

                            if (v.srfSpeed >= 0.1)
                            {
                                double velAngle = 90 - Math.Acos(Vector3d.Dot(v.srf_velocity.normalized, v.upAxis)) * 180.0 / Math.PI;

                                GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 20f), "Velocity pitch angle:", nameStyle);
                                GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 20f),
                                    v.state != Vessel.State.DEAD ? velAngle.ToString("N2") : "N/A", valueStyle);
                                ybase += 18f;
                            }
                        }
                        if(Config.hudCoordinates&&v.mainBody==wpd.celestialBody)
                        {
                            ybase += 9;
                            GUI.Label(new Rect((float)Screen.width / 2.0f - 188f, ybase, 240f, 38f), "Coordinates of " + label + ":", nameStyle);
                            GUI.Label(new Rect((float)Screen.width / 2.0f + 60f, ybase, 120f, 38f),
                                v.state != Vessel.State.DEAD ? string.Format("{0}\r\n{1}", Util.DecimalDegreesToDMS(wpd.waypoint.latitude,true), Util.DecimalDegreesToDMS(wpd.waypoint.longitude,false)) : "N/A", valueStyle);
                            ybase += 18f;
                        }
                    }
                }
            }

            // Don't draw the waypoint
            if (Config.waypointDisplay == Config.WaypointDisplay.NONE)
            {
                return;
            }

            // Translate to scaled space
            Vector3d localSpacePoint = celestialBody.GetWorldSurfacePosition(wpd.waypoint.latitude, wpd.waypoint.longitude, wpd.waypoint.height + wpd.waypoint.altitude);
            Vector3d scaledSpacePoint = ScaledSpace.LocalToScaledSpace(localSpacePoint);

            // Don't draw if it's behind the camera
            if (Vector3d.Dot(MapView.MapCamera.camera.transform.forward, scaledSpacePoint.normalized) < 0.0)
            {
                return;
            }

            // Translate to screen position
            Vector3 screenPos = MapView.MapCamera.camera.WorldToScreenPoint(new Vector3((float)scaledSpacePoint.x, (float)scaledSpacePoint.y, (float)scaledSpacePoint.z));

            // Draw the marker at half-resolution (30 x 45) - that seems to match the one in the map view
            Rect markerRect = new Rect(screenPos.x - 15f, (float)Screen.height - screenPos.y - 45.0f, 30f, 45f);

            // Set the window position relative to the selected waypoint
            if (selectedWaypoint == wpd.waypoint)
            {
                windowPos = new Rect(markerRect.xMin - 97, markerRect.yMax + 12, 224, 60);
            }

            // Handling clicking on the waypoint
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (markerRect.Contains(Event.current.mousePosition))
                {
                    selectedWaypoint = wpd.waypoint;
                    windowPos = new Rect(markerRect.xMin - 97, markerRect.yMax + 12, 224, 60);
                    waypointName = label;
                    newClick = false;
                }
                else if (newClick)
                {
                    selectedWaypoint = null;
                }
            }

            // Only handle on repaint events
            if (Event.current.type == EventType.Repaint)
            {
                // Half-res for the icon too (16 x 16)
                Rect iconRect = new Rect(screenPos.x - 8f, (float)Screen.height - screenPos.y - 39.0f, 16f, 16f);

                // Draw the marker
                Graphics.DrawTexture(markerRect, GameDatabase.Instance.GetTexture("Squad/Contracts/Icons/marker", false), new Rect(0.0f, 0.0f, 1f, 1f), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f * (wpd.currentAlpha - 0.3f) / 0.7f));

                // Draw the icon, but support blinking
                if (!Util.IsNavPoint(wpd.waypoint) || !FinePrint.WaypointManager.navWaypoint.blinking || (int)((Time.fixedTime - (int)Time.fixedTime) * 4) % 2 == 0)
                {
                    Graphics.DrawTexture(iconRect, ContractDefs.textures[wpd.waypoint.id], new Rect(0.0f, 0.0f, 1f, 1f), 0, 0, 0, 0, SystemUtilities.RandomColor(wpd.waypoint.seed, wpd.currentAlpha));
                }

                // Hint text!
                if (iconRect.Contains(Event.current.mousePosition))
                {
                    // Add agency to label
                    if (wpd.waypoint.contractReference != null)
                    {
                        label += "\n" + wpd.waypoint.contractReference.Agent.Name;
                    }
                    float width = 240f;
                    float height = hintTextStyle.CalcHeight(new GUIContent(label), width);
                    float yoffset = height + 48.0f;
                    GUI.Box(new Rect(screenPos.x - width/2.0f, (float)Screen.height - screenPos.y - yoffset, width, height), label, hintTextStyle);
                }
            }
        }

        private void ShowNavigationWindow()
        {
            if (selectedWaypoint != null)
            {
                GUI.skin = HighLogic.Skin;
                windowPos = GUILayout.Window(10, windowPos, NavigationWindow, waypointName, GUILayout.MinWidth(224));
            }
        }

        private void NavigationWindow(int windowID)
        {
            if (selectedWaypoint == null)
            {
                return;
            }

            GUILayout.BeginVertical();
            if (!Util.IsNavPoint(selectedWaypoint))
            {
                if (GUILayout.Button("Activate Navigation", HighLogic.Skin.button, GUILayout.ExpandWidth(true)))
                {
                    FinePrint.WaypointManager.setupNavPoint(selectedWaypoint);
                    FinePrint.WaypointManager.activateNavPoint();
                    selectedWaypoint = null;
                }
            }
            else
            {
                if (GUILayout.Button("Deactivate Navigation", HighLogic.Skin.button, GUILayout.ExpandWidth(true)))
                {
                    FinePrint.WaypointManager.clearNavPoint();
                    selectedWaypoint = null;
                }

            }
            if (CustomWaypoints.Instance.IsCustom(selectedWaypoint))
            {
                if (GUILayout.Button("Edit Custom Waypoint", HighLogic.Skin.button, GUILayout.ExpandWidth(true)))
                {
                    CustomWaypointGUI.EditWaypoint(selectedWaypoint);
                    selectedWaypoint = null;
                }
                if (GUILayout.Button("Delete Custom Waypoint", HighLogic.Skin.button, GUILayout.ExpandWidth(true)))
                {
                    CustomWaypointGUI.DeleteWaypoint(selectedWaypoint);
                    selectedWaypoint = null;
                }
            }
            GUILayout.EndVertical();
        }

        /// <summary>
        /// Calculates the time to the distance based on the vessels srfSpeed and transform it to a readable string.
        /// </summary>
        /// <param name="waypoint">The waypoint</param>
        /// <param name="distance">Distance in meters</param>
        /// <returns></returns>
        protected string GetTimeToWaypoint(WaypointData wpd)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v.srfSpeed < 0.1)
            {
                return null;
            }

            double time = (wpd.distanceToActive / v.horizontalSrfSpeed);

            // Earthtime
            uint SecondsPerYear = 31536000; // = 365d
            uint SecondsPerDay = 86400;     // = 24h
            uint SecondsPerHour = 3600;     // = 60m
            uint SecondsPerMinute = 60;     // = 60s

            if (GameSettings.KERBIN_TIME == true)
            {
                SecondsPerYear = 9201600;  // = 426d
                SecondsPerDay = 21600;     // = 6h
                SecondsPerHour = 3600;     // = 60m
                SecondsPerMinute = 60;     // = 60s
            }

            int years = (int)(time / SecondsPerYear);
            time -= years * SecondsPerYear;

            int days = (int)(time / SecondsPerDay);
            time -= days * SecondsPerDay;

            int hours = (int)(time / SecondsPerHour);
            time -= hours * SecondsPerHour;

            int minutes = (int)(time / SecondsPerMinute);
            time -= minutes * SecondsPerMinute;

            int seconds = (int)(time);

            string output = "";
            if (years != 0)
            {
                output += years + "y";
            }
            if (days != 0)
            {
                if (output.Length != 0) output += ", ";
                output += days + "d";
            }
            if (hours != 0 || minutes != 0 || seconds != 0 || output.Length == 0)
            {
                if (output.Length != 0) output += ", ";
                output += hours.ToString("D2") + ":" + minutes.ToString("D2") + ":" + seconds.ToString("D2");
            }

            return output;
        }

        public void HandleClick(WaypointData wpd)
        {
            // Translate to screen position
            Vector3d localSpacePoint = wpd.celestialBody.GetWorldSurfacePosition(wpd.waypoint.latitude, wpd.waypoint.longitude, wpd.waypoint.altitude);
            Vector3d scaledSpacePoint = ScaledSpace.LocalToScaledSpace(localSpacePoint);
            Vector3 screenPos = MapView.MapCamera.camera.WorldToScreenPoint(new Vector3((float)scaledSpacePoint.x, (float)scaledSpacePoint.y, (float)scaledSpacePoint.z));

            Rect markerRect = new Rect(screenPos.x - 15f, (float)Screen.height - screenPos.y - 45.0f, 30f, 45f);

            if (markerRect.Contains(Event.current.mousePosition))
            {
                selectedWaypoint = wpd.waypoint;
                windowPos = new Rect(markerRect.xMin - 97, markerRect.yMax + 12, 224, 60);
                waypointName = wpd.waypoint.name + (wpd.waypoint.isClustered ? (" " + StringUtilities.IntegerToGreek(wpd.waypoint.index)) : "");
                newClick = false;
            }
            else if (newClick)
            {
                selectedWaypoint = null;
            }
        }

        public void HintText(WaypointData wpd)
        {
            // Translate to screen position
            Vector3d localSpacePoint = wpd.celestialBody.GetWorldSurfacePosition(wpd.waypoint.latitude, wpd.waypoint.longitude, wpd.waypoint.altitude);
            Vector3d scaledSpacePoint = ScaledSpace.LocalToScaledSpace(localSpacePoint);
            Vector3 screenPos = MapView.MapCamera.camera.WorldToScreenPoint(new Vector3((float)scaledSpacePoint.x, (float)scaledSpacePoint.y, (float)scaledSpacePoint.z));

            Rect iconRect = new Rect(screenPos.x - 8f, (float)Screen.height - screenPos.y - 39.0f, 16f, 16f);

            // Hint text!
            if (iconRect.Contains(Event.current.mousePosition))
            {
                string label = wpd.waypoint.name + (wpd.waypoint.isClustered ? (" " + StringUtilities.IntegerToGreek(wpd.waypoint.index)) : "");
                float width = 240f;
                float height = hintTextStyle.CalcHeight(new GUIContent(label), width);
                float yoffset = height + 48.0f;
                GUI.Box(new Rect(screenPos.x - width / 2.0f, (float)Screen.height - screenPos.y - yoffset, width, height), label, hintTextStyle);
            }
        }
    }
}
