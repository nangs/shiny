using System;
using System.Linq;
using System.Reactive.Linq;
using System.Collections.Generic;
using Shiny.BluetoothLE.Internals;
using Android;
using Android.Bluetooth;


namespace Shiny.BluetoothLE
{
    public class BleManager : AbstractBleManager,
                              ICanControlAdapterState,
                              ICanSeePairedPeripherals
    {
        public const string BroadcastReceiverName = "com.shiny.bluetoothle.ShinyBleCentralBroadcastReceiver";
        readonly CentralContext context;


        public BleManager(CentralContext context) => this.context = context;


        bool isScanning;
        public override bool IsScanning => this.isScanning;


        public override IObservable<IPeripheral?> GetKnownPeripheral(string peripheralUuid)
        {
            //var native = this.context.Manager.Adapter.GetRemoteDevice(peripheralUuid
            //    .ToByteArray()
            //    .Skip(10)
            //    .Take(6)
            //    .ToArray()
            //);
            //if (native == null)
            //    return Observable.Return<IPeripheral?>(null);

            //var peripheral = this.context.GetDevice(native);
            //return Observable.Return(peripheral);
            return Observable.Return<IPeripheral?>(null);
        }


        public override IObservable<IEnumerable<IPeripheral>> GetConnectedPeripherals(string? serviceUuid = null)
            => Observable.Return(this.context
                .Manager
                .GetConnectedDevices(ProfileType.Gatt)
                .Select(this.context.GetDevice)
            );


        public override IObservable<AccessState> RequestAccess() => Observable.FromAsync(async () =>
        {
            if (!this.context.Android.IsInManifest(Manifest.Permission.Bluetooth))
                return AccessState.NotSetup;

            if (!this.context.Android.IsInManifest(Manifest.Permission.BluetoothAdmin))
                return AccessState.NotSetup;

            var forBackground = this.context.Services.GetService(typeof(IBleDelegate)) != null;
            var result = this.context.Android.IsMinApiLevel(29) && forBackground
                ? await this.context.Android.RequestAccess(Manifest.Permission.AccessBackgroundLocation, Manifest.Permission.AccessFineLocation)
                : await this.context.Android.RequestAccess(Manifest.Permission.AccessFineLocation);

            // TODO: check if location is enabled?
            if (result != AccessState.Available)
                return result;

            return this.Status;
        });


        public override AccessState Status => this.context.Status;
        public override IObservable<AccessState> WhenStatusChanged() => this.context
            .StatusChanged
            .StartWith(this.Status);


        public override IObservable<ScanResult> Scan(ScanConfig? config = null) => Observable.Create<ScanResult>(async ob =>
        {
            if (this.IsScanning)
                throw new ArgumentException("There is already an active scan");

            IDisposable? sub = null;
            var result = await this.RequestAccess();
            if (result != AccessState.Available)
            {
                ob.OnError(new PermissionException(BleLogCategory.BluetoothLE, result));
            }
            else
            {
                this.isScanning = true;
                sub = this.context
                    .Scan(config ?? new ScanConfig())
                    .Finally(() => this.isScanning = false)
                    .Subscribe(
                        ob.OnNext,
                        ob.OnError
                    );
            }
            return () => sub?.Dispose();
        });


        public override void StopScan()
        {
            if (!this.IsScanning)
                return;

            this.isScanning = false;
            this.context.StopScan();
        }


        public IObservable<IEnumerable<IPeripheral>> GetPairedPeripherals() => Observable.Return(this.context
            .Manager
            .Adapter
            .BondedDevices
            .Where(x => x.Type == BluetoothDeviceType.Dual || x.Type == BluetoothDeviceType.Le)
            .Select(this.context.GetDevice)
        );


        public void SetAdapterState(bool enable)
        {
            if (enable && !BluetoothAdapter.DefaultAdapter.IsEnabled)
                BluetoothAdapter.DefaultAdapter.Enable();

            else if (!enable && BluetoothAdapter.DefaultAdapter.IsEnabled)
                BluetoothAdapter.DefaultAdapter.Disable();
        }
    }
}