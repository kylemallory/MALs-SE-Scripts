using Sandbox.Game.Gui;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;
using VRage.Game;
using VRage;
using VRage.Game.Entity;

namespace IngameScript
{
    public partial class Program
    {
        public class Threat
        {
            WcPbApi wcApi;                      // the weapon core api
            MyDetectedEntityInfo entityInfo;    // the detected entity/grid
            double wcThreatLevel = 0.0;         // the threat-level that WeaponCore generates
            IWeapon detectingWeapon;            // the weapon which detected this grid (if known)
            IMyProgrammableBlock detector;      // the instance of this script which detected the entity

            public Threat(WcPbApi wcApi_, IMyProgrammableBlock Me, MyDetectedEntityInfo entity, double threatLevel, IWeapon weapon = null)
            {
                wcApi = wcApi_;
                entityInfo = entity;
                detector = Me;
                wcThreatLevel = threatLevel;
                detectingWeapon = weapon;
            }

            public string Name { get { return entityInfo.Name; } }
            public long EntityId { get { return entityInfo.EntityId; } }

            public MyRelationsBetweenPlayerAndBlock Relationship { get { return entityInfo.Relationship; } }

            public double Speed { get { return entityInfo.Velocity.Length(); } }

            public Vector3D Position { get { return entityInfo.Position; } }

            public Vector3D Velocity { get { return entityInfo.Velocity; } }

            public MyDetectedEntityType Type { get { return entityInfo.Type; } }

            public double offenseRating { get { return wcThreatLevel; } }
            // public double hasShields { get { return api.; } }

            public double effectiveDPS { get { return wcApi.GetConstructEffectiveDps(EntityId); } }
            public double optimalDPS { get { return wcApi.GetOptimalDps(EntityId); } }

            public double Distance { get { return (Position - detector.GetPosition()).Length(); } }

            public Vector3D InterceptPoint
            {
                get
                {
                    return Position + (Velocity.Normalized() * (Position - detector.GetPosition()).Length() * InterceptFactor);
                }
            }

            public Vector3D InterceptPointRel { get { return detector.GetPosition() - InterceptPoint; } }

            public double ClosestApproach { get { return InterceptPointRel.Length(); } }

            /**
             * returns a factor value the is a exponential value from 0 to 1, driven by the distance to this threat.
             * the factor is calculated as a curve between the nearest distance, which returns a 1, and the farthest distance, which returns a 0.
             * Curve introduces an non-linear bias to the factor. Recommended values are between 0.5 and 2.0.
             */
            public double DistanceFactor
            {
                get
                {
                    if (distanceNear < 0) distanceNear = 0;
                    if (distanceFar > 25000) distanceFar = 25000;

                    if (Distance < distanceNear) return 1.0;
                    if (Distance > distanceFar) return 0.0;

                    return 1 - Math.Pow((Distance - distanceNear) / (distanceFar - distanceNear), distanceWeight);
                }
            }

            /**
             * returns a factor value the is a exponential value from 0 to 1, driven by speed of this threat.
             * the factor is calculated as a curve between the nearest distance, which returns a 1, and the farthest distance, which returns a 0.
             * Curve introduces an non-linear bias to the factor. Recommended values are between 0.5 and 2.0.
             */
            public double SpeedFactor
            {
                get
                {
                    if (speedSlow < 0) speedSlow = 0;
                    if (speedFast > 2500) speedFast = 2500;

                    if (Speed < speedSlow) return 0.0;
                    if (Speed > speedFast) return 1.0;

                    return Math.Pow((Speed - speedSlow) / (speedFast - speedSlow), speedCurve);
                }
            }

            public double InterceptFactor
            {
                get
                {
                    //new Vector3D(entityInfo.Velocity.Normalized()).Dot((detector.GetPosition() - Position).Normalized());
                    double f = Velocity.Normalized().Dot((detector.GetPosition() - Position).Normalized());
                    return double.IsNaN(f) ? 0 : f;
                }
            }



            /**
             * returns a factor value the is a exponential value from 0 to 1, driven by the "Closest Approach" distance that this threat will reach.
             * the factor is calculated as a curve between the nearest distance, which returns a 1, and the farthest distance, which returns a 0.
             * Curve introduces an non-linear bias to the factor. Recommended values are between 0.5 and 2.0.
             */
            public double ApproachFactor
            {
                get
                {
                    if (approachNear < 0) approachNear = 0;
                    if (approachFar > 25000) approachFar = 25000;

                    if (ClosestApproach < approachNear) return 1.0;
                    if (ClosestApproach > approachFar) return 0.0;

                    return (1 - Math.Pow((ClosestApproach - approachNear) / (approachFar - approachNear), approachCurve)) * approachWeight;
                }
            }


            /**
             * returns a factor value the is a exponential value from 0 to 1, driven by Weapon Core Offensive Rating (not the UI threat-level).
             * the factor is calculated as a curve between 0 (which returns 0) and the maximum offensive rating (which returns 1).
             * Curve introduces an non-linear bias to the factor. Recommended values are between 0.5 and 2.0.
             */
            public double OffRatFactor
            {
                get
                {
                    if (offRatMax > 1000) offRatMax = 1000;

                    if (offenseRating < 0) return 0.0;
                    if (offenseRating > offRatMax) return 1.0;

                    return Math.Pow(offenseRating / offRatMax, offRatCurve);
                }
            }

            public double DPSFactor
            {
                get
                {
                    return Math.Pow(effectiveDPS / wcApi.GetOptimalDps(detector.CubeGrid.EntityId), dpsCurve);
                }
            }


            public double getDPSFactor(double max_dps, double curve) {
                // return Math.Pow(effectiveDPS / max_dps, curve);
                return optimalDPS / max_dps;
            }

            public double getOverallFactor(bool useOffRat=false)
            {
                double total = 0.0;
                double sum = 0;
                int num = 0;

                if (Speed > 0)
                {
                    // the speed factor is further factored by the intercept factor (moving fast away is not a threat... moving fast towards, is a threat!)
                    sum += SpeedFactor;
                    num++;
                }

                if (Distance > 0)
                {
                    sum += DistanceFactor;
                    num++;
                }

                if (!double.IsNaN(ClosestApproach))
                {
                    sum += ApproachFactor;
                    num++;
                }

                if (!double.IsNaN(InterceptFactor))
                {
                    // the InterceptFactor is normally -1 to 1, but we want it in the range of 0 to 1.
                    sum += (InterceptFactor + 1.0) / 2.0 * interceptWeight;
                    num++;
                }

                if (useOffRat)
                {
                    // the overall factor is a product of the OffenseRating factor.
                    total = sum + OffRatFactor;
                } else
                {
                    var myDps = wcApi.GetOptimalDps(detector.CubeGrid.EntityId);
                    total = sum + getDPSFactor(myDps, 1); //  (getDPSFactor(myDps, 1) * dpsWeight);
                }
                return total;
            }

            public string NamedIntercept { get
                {
                    if (Speed > 0)
                    {
                        if (InterceptFactor < -0.6) return "Rapidly Diverging";
                        else if (InterceptFactor < -0.2) return "Diverging";
                        else if (InterceptFactor < 0.2) return "Tracking";
                        else if (InterceptFactor < 0.6) return "Converging";
                        else return "Rapidly Converging";
                    }
                    else return "Static";
                }
            }

            public double ThreatAssessment
            {
                get
                {
                    // a set of conditions to rank a threat from 0 (None), 1 (Low), 2 (Moderate), 3 (High), 4 (Severe), 
                    // it is hard to build a mathematical model to do this, so this property is more about logic and conditions
                    // however, these can be cummulative as well

                    return 0.0;
                }
            }

            public new MyTuple<byte, long, Vector3D, double> GetIIFTuple()
            {
                /*
                Send an IGC broadcast message with tag IGC_IFF_MSG and content MyTuple<byte, long, Vector3D, double>.
                    byte: Target info flags.To combine flags, simply add the numbers together.
                            Flag values: Neutral = 0, Enemy = 1, Friendly = 2, Locked = 4, LargeGrid = 8, SmallGrid = 16, Missile = 32, Asteroid = 64.
                    long: Entity ID of the target.
                    Vector3D: Position of the target.
                    double: Squared radius of the target.Used by the turret slaver for friendly fire avoidance
                */
                int flags = 0; // always assume neutral until we discover otherwise
                flags += (Relationship == MyRelationsBetweenPlayerAndBlock.Enemies ? 1 : 0); // if enemy
                flags += (Relationship == MyRelationsBetweenPlayerAndBlock.Friends ? 2 : 0); // if friendly

                flags += (Type == MyDetectedEntityType.LargeGrid ? 8 : 0); // if large grid
                flags += (Type == MyDetectedEntityType.SmallGrid ? 16 : 0); // if small grid
                flags += (Type == MyDetectedEntityType.Missile ? 16 : 0); // if missile
                flags += (Type == MyDetectedEntityType.Asteroid ? 16 : 0); // if asteroid

                return new MyTuple<byte, long, Vector3D, double>((byte)flags, EntityId, Position, 0.0);
            }

            public string ToString(int detail=0, List<IWeapon> weapons=null)
            {
                if (detail>0)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("---------------------------");

                    sb.AppendLine($"Name: {Name} [{Type}] :: {getOverallFactor():0.###}");
                    sb.AppendLine($" -> Distance: {Distance:0.} meters");
                    if (Speed > 0)
                    {
                        sb.AppendLine($" -> Speed: {Speed:0.} m/s");
                        sb.AppendLine($" -> Intercept: {NamedIntercept} ({InterceptFactor:0.###})");
                        if (InterceptFactor > 0) sb.AppendLine($" -> Closest approach: {ClosestApproach:0} meters");
                    }
                    sb.AppendLine($" -> Factors:");
                    if (detail > 1)
                    {
                        long myEntId = detector.CubeGrid.EntityId;
                        sb.AppendLine($"   - Effective DPS: {effectiveDPS:0.###} (vs {wcApi.GetOptimalDps(myEntId)})");
                        sb.AppendLine($"   - DPS Factor: {DPSFactor:0.###}  (DPS: {optimalDPS:0})");
                        sb.AppendLine($"   - Offensive Rating: {OffRatFactor:0.###}  (OffRat: {offenseRating:0.###})");
                        sb.AppendLine($"   - Distance Factor: {DistanceFactor:0.###}");
                        sb.AppendLine($"   - Speed Factor: {SpeedFactor:0.###}");
                        sb.AppendLine($"   - Approach Factor: {ApproachFactor:0.###}");
                    }

                    // Determine which weapons are targeting this entity...
                    if (weapons != null)
                    {
                        sb.AppendLine($" -> Targeted by:");
                        foreach (IWeapon w in weapons)
                        {
                            if (w.getTarget().Equals(EntityId)) sb.AppendLine($"   - {w.Name}");
                        }
                    }
                    sb.AppendLine();

                    return sb.ToString();
                } else
                    return $"{Name} :: {getOverallFactor():0.###} ({Distance:0}m, {offenseRating:0.###} o/r)";
            }

            public override string ToString()
            {
                return ToString(0, null);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 19;
                    {
                        hash = hash * 31 + this.EntityId.GetHashCode();
                        hash = hash * 31 + (int)Math.Round(this.ThreatAssessment * 1000.0);
                    }
                    return hash;
                }
            }


        }

    }
}
