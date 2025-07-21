using Core;
using Game;
using Game.State;
using HarmonyLib;
using JetBrains.Annotations;
using KeyValue.Runtime;
using Model.Ops.Definition;
using Network;
using Railloader;
using Serilog;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UI.Builder;
using UnityEngine;


namespace Model.Ops
{
    public class InterchangedIndustryUnloader : IndustryComponent
    {
        private Interchange _interchange;

        private bool? _hasInterchange;

        public Load load;

        [SerializeField]
        private Ledger.Category ledgerCategory;

        public override string DisplayName
        {
            get
            {
                Interchange interchange = Interchange;
                if (interchange == null)
                {
                    return base.name;
                }
                return interchange.DisplayName + " to " + base.name;
            }
        }

        internal string InterchangeName
        {
            get
            {
                if (!(Interchange == null))
                {
                    return Interchange.DisplayName;
                }
                return null;
            }
        }

        [CanBeNull]
        private Interchange Interchange
        {
            get
            {
                if (!_hasInterchange.HasValue)
                {
                    _interchange = base.Industry.GetComponentInChildren<Interchange>();
                    _hasInterchange = _interchange != null;
                }
                return _interchange;
            }
        }

        private string KeyBardoCars => "br-" + subIdentifier;

        private Hyperlink HyperlinkToThis => Hyperlink.To(base.Industry, base.name);

        public override bool WantsAutoDestination(AutoDestinationType type)
        {
            return type == AutoDestinationType.Load;
        }

        public override void Service(IIndustryContext ctx)
        {
        }

        public override void OrderCars(IIndustryContext ctx)
        {
            Interchange componentInChildren = base.Industry.GetComponentInChildren<Interchange>();
            foreach (var item in EnumerateBardoCars())
            {
                var (carId, _) = item;
                if (!(item.returnTime > ctx.Now))
                {
                    componentInChildren.OrderReturnFromBardo(carId);
                }
            }
        }

        public void ServeInterchange(IIndustryContext ctx, Interchange interchange)
        {
            StateManager shared = StateManager.Shared;
            List<IOpsCar> list = (from car in EnumerateCars(ctx, requireWaybill: true)
                                  where car.IsFull(load)
                                  select car).ToList();
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            GameDateTime returnTime = ctx.Now.AddingDays(23f / 24f);
            foreach (IOpsCar item3 in list)
            {
                (float quantity, float capacity) tuple = item3.QuantityOfLoad(load);
                float item = tuple.quantity;
                float item2 = tuple.capacity;
                int num4 = Mathf.RoundToInt(item * load.payPerQuantity);
                if (num4 > 0)
                {
                    num2 += num4;
                    num3++;
                }
                item3.Unload(load, item2);
                item3.SetWaybill(null, this, "Empty completed");
                ctx.MoveToBardo(item3);
                ScheduleReturnFromBardo(item3, returnTime);
            }
            if (num2 > 0)
            {
                base.Industry.ApplyToBalance(num2, ledgerCategory, null, num3, quiet: true);
                Multiplayer.Broadcast(string.Format("Sold {0} of {1} at {2} for {3:C0}. Expected return: {4}.", num3.Pluralize("car"), load.description, HyperlinkToThis, num2, 1.Pluralize("day")));
            }
        }

        // Harmony stuff starts here

        private readonly PropertyInfo p_KeyValueObject = AccessTools.Property(typeof(Industry), "KeyValueObject");

        private readonly PropertyInfo p_Item = AccessTools.Property(typeof(IKeyValueObject), "Item");
        private Value GetKeyValueObject(string key) => (Value)p_Item.GetValue(p_KeyValueObject.GetValue(base.Industry), [key]);
        private void SetKeyValueObject(string key, Value value) => p_Item.SetValue(p_KeyValueObject.GetValue(base.Industry), value, [key]);

        // Harmony stuff ends here
        private void SetBardoCarsValue(string key, Value value)
        {
            IReadOnlyDictionary<string, Value> dictionaryValue = GetKeyValueObject(KeyBardoCars).DictionaryValue;
            if (dictionaryValue.TryGetValue(key, out var value2))
            {
                if (value2.Equals(value))
                {
                    return;
                }
            }
            else if (value.IsNull)
            {
                return;
            }
            Dictionary<string, Value> dictionary = new Dictionary<string, Value>((IDictionary<string, Value>)dictionaryValue);
            if (value.IsNull)
            {
                dictionary.Remove(key);
            }
            else
            {
                dictionary[key] = value;
            }
            SetKeyValueObject(KeyBardoCars, (dictionary.Any() ? Value.Dictionary(dictionary) : Value.Null()));
        }

        private void ScheduleReturnFromBardo(IOpsCar car, GameDateTime returnTime)
        {
            SetBardoCarsValue(car.Id, (int)returnTime.TotalSeconds);
        }

        public void InterchangeDidFillReturnFromBardoOrder(string carId)
        {
            SetBardoCarsValue(carId, Value.Null());
        }

        private IEnumerable<(string carId, GameDateTime returnTime)> EnumerateBardoCars()
        {
            Value value = GetKeyValueObject(KeyBardoCars);
            IReadOnlyDictionary<string, Value> dictionaryValue = value.DictionaryValue;
            foreach (KeyValuePair<string, Value> item2 in dictionaryValue)
            {
                string item = item2.Key;
                Value value2 = item2.Value;
                yield return (carId: item, returnTime: new GameDateTime(value2.FloatValue));
            }
        }

        public override void EnsureConsistency()
        {
            if (!base.ProgressionDisabled && !base.Industry.ProgressionDisabled)
            {
                return;
            }
            TrainController shared = TrainController.Shared;
            OpsController shared2 = OpsController.Shared;
            foreach (var item3 in EnumerateBardoCars())
            {
                string item = item3.carId;
                GameDateTime item2 = item3.returnTime;
                if (shared.TryGetCarForId(item, out var car) && !(car.Bardo != base.Identifier))
                {
                    Vector3 thisPosition = base.transform.position;
                    InterchangedIndustryUnloader interchangedIndustryUnloader = (from iil in shared2.EnabledInterchanges.SelectMany((Interchange interchange) => interchange.Industry.GetComponentsInChildren<InterchangedIndustryUnloader>())
                                                                                 where iil != this
                                                                                 orderby Vector3.Distance(iil.transform.position, thisPosition)
                                                                                 select iil).FirstOrDefault();
                    if (interchangedIndustryUnloader == null)
                    {
                        Log.Warning("Couldn't find another InterchangedIndustryUnloader to move car {car} to; clearing bardo.", car);
                        car.Bardo = null;
                    }
                    else
                    {
                        Log.Information("Retargeting bardo car {car} to {other}", car, interchangedIndustryUnloader);
                        car.Bardo = interchangedIndustryUnloader.Identifier;
                        interchangedIndustryUnloader.ScheduleReturnFromBardo(new OpsCarAdapter(car, shared2), item2);
                    }
                }
            }
            SetKeyValueObject(KeyBardoCars, null);
        }

        public override void BuildPanel(UIPanelBuilder builder)
        {
            builder.AddSection("Via Interchange: " + base.name + " - " + load.description, delegate (UIPanelBuilder uIPanelBuilder)
            {
                float nominalQuantityPerCarLoad = load.NominalQuantityPerCarLoad;
                uIPanelBuilder.AddField("Expects Car Types", carTypeFilter.ToString());
                uIPanelBuilder.AddField("Price", $"{Mathf.RoundToInt(nominalQuantityPerCarLoad * load.payPerQuantity):C0} per {load.QuantityString(nominalQuantityPerCarLoad)} car");
            });
        }
    }

    public static class InterchangeExtensions
    {
        public static InterchangedIndustryUnloader[] Unloaders(this Interchange interchange) => interchange.Industry.GetComponentsInChildren<InterchangedIndustryUnloader>();

        public static void ServeInterchangedIndustryUnloaders(this Interchange interchange, GameDateTime now)
        {
            InterchangedIndustryUnloader[] unloaders = Unloaders(interchange);
            foreach (InterchangedIndustryUnloader obj in unloaders)
            {
                IndustryContext industryContext = obj.CreateContext(now, 0f);
                obj.ServeInterchange(industryContext, interchange);
            }
        }
    }




}

namespace InterchangedIndustryUnloaderPlugin
{
    public class InterchangedIndustryUnloaderPlugin : SingletonPluginBase<InterchangedIndustryUnloaderPlugin>, IModTabHandler
    {
        public InterchangedIndustryUnloaderPlugin()
        {
            new Harmony("NS15.InterchangedIndustryUnloader").PatchAll();
        }
        public void ModTabDidClose()
        {
        }

        public void ModTabDidOpen(UIPanelBuilder builder)
        {
        }
    }

}
