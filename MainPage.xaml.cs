// Copyright 2021 Jacob Zhang. All Rights Reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Devices.Enumeration;
using Windows.Devices.Midi;
using Windows.Data.Xml.Dom;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Notifications;
using Windows.UI.Xaml.Media;
using Windows.Storage;

namespace MidiPageTurner
{
    enum MidiEventType : byte
    {
        NONE,
        NOTEONOFF,
        CONTROLCHANGE
    };

    public sealed partial class MainPage : Page
    {
        ApplicationDataContainer _LocalSettings = ApplicationData.Current.LocalSettings;

        private string _deviceSelectorString;
        public DeviceInformationCollection DeviceInformationCollection { get; set; }
        private MidiInPort _midiInPort;
        private string _currentDevice;

        private readonly InputInjector _inputInjector;

        private readonly byte _triggerThreshold = 20;

        // Minimum and maximum values for transmitted midi signal, depending on type
        private readonly byte[] _minValues = { 0, 0, 0 };
        private readonly byte[] _maxValues = { 0, 100, 127 };

        private readonly VirtualKey[][] _pageTurnKeyOptions1 =
            {new[] {VirtualKey.Right}, new[] {VirtualKey.Down}, new[] {VirtualKey.PageDown}, new[] {VirtualKey.Space}};

        private readonly VirtualKey[][] _pageTurnKeyOptions2 =
            {new[] {VirtualKey.Left}, new[] {VirtualKey.Up}, new[] {VirtualKey.PageUp}, new[] {VirtualKey.Shift, VirtualKey.Space}};

        private byte _currentMidiTriggerChannel1 = 0;
        private MidiEventType _currentMidiTriggerEvent1 = MidiEventType.NONE;
        private byte _debounceActivateInput1 = 0;
        private byte _debounceDeactivateInput1 = 0;
        private bool _capturingMidiInput1 = false;
        private string _displayString1 = "";

        private byte _currentMidiTriggerChannel2 = 0;
        private MidiEventType _currentMidiTriggerEvent2 = MidiEventType.NONE;
        private byte _debounceActivateInput2 = 0;
        private byte _debounceDeactivateInput2 = 0;
        private bool _capturingMidiInput2 = false;
        private string _displayString2 = "";

        private VirtualKey[] _currentPageTurnKey1;
        private VirtualKey[] _currentPageTurnKey2;

        private readonly SolidColorBrush _activeBrush = new SolidColorBrush(Color.FromArgb(255, 39, 174, 96));
        private readonly SolidColorBrush _inactiveBrush = new SolidColorBrush(Color.FromArgb(255, 189, 195, 199));
        private DateTime _lastTriggerTime = DateTime.Now;
        private readonly TimeSpan _cooldown = TimeSpan.FromMilliseconds(750);

        public MainPage()
        {
            InitializeComponent();
            InitDeviceWatcher();
            _inputInjector = InputInjector.TryCreate();
            SetBadge("unavailable");

            // uncomment if saved settings get messed up
            //ResetSettings();

            LoadSettings();
            SetDisplayStrings();
        }

        public void InitDeviceWatcher()
        {
            _deviceSelectorString = MidiInPort.GetDeviceSelector();

            var deviceWatcher = DeviceInformation.CreateWatcher(_deviceSelectorString);
            deviceWatcher.Added += async (sender, args) =>
            {
                Log("Device added");
                await Dispatcher.RunAsync(CoreDispatcherPriority.High, UpdateDevices);
            };

            deviceWatcher.Removed += async (sender, args) =>
            {
                Log("Device removed");
                if (args.Id == _currentDevice)
                {
                    Stop();
                }
                await Dispatcher.RunAsync(CoreDispatcherPriority.High, UpdateDevices);
            };

            deviceWatcher.Updated += async (sender, args) =>
            {
                Log("Device updated");
                await Dispatcher.RunAsync(CoreDispatcherPriority.High, UpdateDevices);
            };

            deviceWatcher.EnumerationCompleted += async (sender, args) =>
            {
                Log("Device enumeration completed");
                await Dispatcher.RunAsync(CoreDispatcherPriority.High, UpdateDevices);
            };

            Log("Starting DeviceWatcher...");
            deviceWatcher.Start();
        }

        public async void UpdateDevices()
        {
            Log("Updating devices...");
            DeviceInformationCollection = await DeviceInformation.FindAllAsync(_deviceSelectorString);

            MidiInListBox.Items.Clear();

            if (!DeviceInformationCollection.Any())
            {
                Log("No MIDI input devices found");
                MidiInListBox.Items.Add("No MIDI input devices found!");
                MidiInListBox.IsEnabled = false;
                return;
            }

            foreach (var deviceInfo in DeviceInformationCollection)
            {
                Log($"Discovered MIDI Input {deviceInfo.Name}");
                MidiInListBox.Items.Add(deviceInfo.Name);
            }
            MidiInListBox.IsEnabled = true;
        }

        private async void MidiInPort_MessageReceived(MidiInPort sender, MidiMessageReceivedEventArgs args)
        {
            var receivedMidiMessage = args.Message;

            // Find message Type and Channel

            MidiEventType msgEventType = MidiEventType.NONE;
            byte msgChannel = 0;
            byte msgValue = 0;
            String msgModeDesc = "";
            String msgModeSpec = "";

            if (receivedMidiMessage is MidiControlChangeMessage controlChangeMessage)
            {
                msgEventType = MidiEventType.CONTROLCHANGE;
                msgChannel = controlChangeMessage.Controller;
                msgValue = controlChangeMessage.ControlValue;
                msgModeDesc = "Control Change";
                msgModeSpec = "Channel";
            }

            if (receivedMidiMessage is MidiNoteOnMessage noteOnMessage)
            {
                msgEventType = MidiEventType.NOTEONOFF;
                msgChannel = noteOnMessage.Note;
                msgValue = noteOnMessage.Velocity;
                msgModeDesc = "Note On/Off";
                msgModeSpec = "Note";
            }
            if (receivedMidiMessage is MidiNoteOffMessage noteOffMessage)
            {
                msgEventType = MidiEventType.NOTEONOFF;
                msgChannel = noteOffMessage.Note;
                msgValue = 0;
                msgModeDesc = "Note On/Off";
                msgModeSpec = "Note";
            }

            if (msgEventType == MidiEventType.NONE)
            {
                return;
            }

            if (_capturingMidiInput1 == true)
            {
                if ((msgChannel != _currentMidiTriggerChannel2) ||
                    (msgEventType != _currentMidiTriggerEvent2))
                {
                    _currentMidiTriggerChannel1 = msgChannel;
                    _currentMidiTriggerEvent1 = msgEventType;
                    _debounceActivateInput1 = (byte)(_maxValues[((int)msgEventType)] - _triggerThreshold);
                    _debounceDeactivateInput1 = (byte)(_minValues[((int)msgEventType)] + _triggerThreshold);
                    _displayString1 = msgModeDesc + ", " + msgModeSpec + ": " + _currentMidiTriggerChannel1;

                    WriteSettings();
                    SetDisplayStrings();

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        Log("Input 1 set to Event " + _displayString1);
                        InputIndicator1.Fill = _activeBrush;
                        TextBlockInputValue1.Text = "" + msgValue;
                    });

                    _capturingMidiInput1 = false;
                    return;
                }
            }

            if (_capturingMidiInput2 == true)
            {
                if ((msgChannel != _currentMidiTriggerChannel1) ||
                    (msgEventType != _currentMidiTriggerEvent1))
                {
                    _currentMidiTriggerChannel2 = msgChannel;
                    _currentMidiTriggerEvent2 = msgEventType;
                    _debounceActivateInput2 = (byte)(_maxValues[((int)msgEventType)] - _triggerThreshold);
                    _debounceDeactivateInput2 = (byte)(_minValues[((int)msgEventType)] + _triggerThreshold);
                    _displayString2 = msgModeDesc + ", " + msgModeSpec + ": " + _currentMidiTriggerChannel2;

                    WriteSettings();
                    SetDisplayStrings();

                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        Log("Input 2 set to Event " + _displayString2);
                        InputIndicator2.Fill = _activeBrush;
                        TextBlockInputValue2.Text = "" + msgValue;
                    });

                    _capturingMidiInput2 = false;
                    return;
                }
            }

            // determine which input is pressed
            bool inputPressed1 = false;
            bool inputPressed2 = false;

            var pageTurnKeys = _currentPageTurnKey1;

            // message fits first input setup
            if (msgEventType == _currentMidiTriggerEvent1 &&
                msgChannel == _currentMidiTriggerChannel1)
            {
                if (msgValue > _debounceActivateInput1)
                {
                    inputPressed1 = true;
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        InputIndicator1.Fill = _activeBrush;
                        TextBlockInputValue1.Text = "" + msgValue;
                    });
                }
                else if (msgValue < _debounceDeactivateInput1)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        InputIndicator1.Fill = _inactiveBrush;
                        TextBlockInputValue1.Text = "" + msgValue;
                    });
                }
            }

            // message fits second input setup
            if (msgEventType == _currentMidiTriggerEvent2 &&
                msgChannel == _currentMidiTriggerChannel2)
            {
                if (msgValue > _debounceActivateInput2)
                {
                    inputPressed2 = true;
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        InputIndicator2.Fill = _activeBrush;
                        TextBlockInputValue2.Text = "" + msgValue;
                    });
                }
                else if (msgValue < _debounceDeactivateInput2)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        InputIndicator2.Fill = _inactiveBrush;
                        TextBlockInputValue2.Text = "" + msgValue;
                    });
                }
            }

            // Cooldown only concerns actual page turns
            if (inputPressed1 == false && inputPressed2 == false ||
               DateTime.Now - _lastTriggerTime < _cooldown)
            {
                return;
            }

            _lastTriggerTime = DateTime.Now;

            if (inputPressed1 == true)
            {
                pageTurnKeys = _currentPageTurnKey1;
            }
            if (inputPressed2 == true)
            {
                pageTurnKeys = _currentPageTurnKey2;
            }

            // Do the actual page turn
            {
                foreach (var key in pageTurnKeys)
                {
                    _inputInjector.InjectKeyboardInput(new[] { new InjectedInputKeyboardInfo
                {
                    VirtualKey = (ushort)key,
                    KeyOptions = InjectedInputKeyOptions.ExtendedKey
                }});
                }

                // release keys again
                foreach (var key in pageTurnKeys.Reverse())
                {
                    _inputInjector.InjectKeyboardInput(new[] { new InjectedInputKeyboardInfo
                {
                    VirtualKey = (ushort)key,
                    KeyOptions = InjectedInputKeyOptions.KeyUp
                }});
                }
            }
        }
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentDevice))
            {
                Stop();
            }

            Log("Obtaining MIDI information on selected device");

            var deviceInformationCollection = DeviceInformationCollection;
            var devInfo = deviceInformationCollection?[MidiInListBox.SelectedIndex];
            if (devInfo == null)
            {
                Log("Error: DeviceInformationCollection was null");
                return;
            }
            _midiInPort = await MidiInPort.FromIdAsync(devInfo.Id);

            if (_midiInPort == null)
            {
                Log("Error: Unable to create MidiInPort from input device");
                return;
            }

            await Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
            {
                _midiInPort.MessageReceived += MidiInPort_MessageReceived;
            });

            Start(devInfo.Id);
        }

        private async void Start(string id)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Log("Subscribing to selected MIDI device");
                _currentDevice = id;
                Indicator.Fill = _activeBrush;
                SetBadge("available");
            });
        }

        private async void Stop()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Log("Unsubscribing from current MIDI device");
                _currentDevice = null;
                Indicator.Fill = _inactiveBrush;
                SetBadge("unavailable");
            });
        }

        private void MidiInListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StartButton.IsEnabled = MidiInListBox.SelectedIndex >= 0;
        }

        private async void Log(string text)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var time = $"({DateTime.Now:HH:mm:ss.ff})\t";
                LogTextBox.Text = string.IsNullOrEmpty(LogTextBox.Text) ? time + text : LogTextBox.Text + "\n" + time + text;
                LogScrollViewer.ChangeView(0.0f, double.MaxValue, 1.0f, true);
            });
        }

        private void SetBadge(string badgeGlyphValue)
        {
            var badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
            var badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
            badgeElement.SetAttribute("value", badgeGlyphValue);
            var badge = new BadgeNotification(badgeXml);
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badge);
        }

        private void CaptureButton1Clicked(object sender, RoutedEventArgs e)
        {
            if (_capturingMidiInput2 == true)
            {
                _capturingMidiInput2 = false;
                _currentMidiTriggerChannel2 = 0;
                _currentMidiTriggerEvent2 = MidiEventType.NONE;
                _debounceActivateInput2 = 0;
                _debounceDeactivateInput2 = 0;
                _displayString2 = "No Input Selected.";
            }

            _currentMidiTriggerChannel1 = 0;
            _currentMidiTriggerEvent1 = MidiEventType.NONE;
            _debounceActivateInput1 = 0;
            _debounceDeactivateInput1 = 0;
            _displayString1 = "Capturing...";

            WriteSettings();
            SetDisplayStrings();

            _capturingMidiInput1 = true;
        }

        private void CaptureButton2Clicked(object sender, RoutedEventArgs e)
        {
            if (_capturingMidiInput1 == true)
            {
                _capturingMidiInput1 = false;
                _currentMidiTriggerChannel1 = 0;
                _currentMidiTriggerEvent1 = MidiEventType.NONE;
                _debounceActivateInput1 = 0;
                _debounceDeactivateInput1 = 0;
                _displayString1 = "No Input Selected.";
            }

            _currentMidiTriggerChannel2 = 0;
            _currentMidiTriggerEvent2 = MidiEventType.NONE;
            _debounceActivateInput2 = 0;
            _debounceDeactivateInput2 = 0;
            _displayString2 = "Capturing...";

            WriteSettings();
            SetDisplayStrings();

            _capturingMidiInput2 = true;
        }

        private void PageTurnKeyListBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPageTurnKey1 = _pageTurnKeyOptions1[PageTurnKeyListBox1.SelectedIndex];
        }

        private void PageTurnKeyListBox2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPageTurnKey2 = _pageTurnKeyOptions2[PageTurnKeyListBox1.SelectedIndex];
        }

        private byte LoadByte(String setting)
        {
            byte result = 0;
            string s = _LocalSettings.Values[setting] as string;
            if (!String.IsNullOrEmpty(s))
                result = byte.Parse(s);
            return result;
        }

        private String LoadString(String setting)
        {
            String result = "";
            string s = _LocalSettings.Values[setting] as string;
            if (!String.IsNullOrEmpty(s))
                result = s;
            return result;
        }

        private async void SetDisplayStrings()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                TextBlockMidiEvent1.Text = _displayString1;
                TextBlockMidiEvent2.Text = _displayString2;
            });
        }

        private void WriteSettings()
        {
            _LocalSettings.Values["_currentMidiTriggerChannel1"] = "" + _currentMidiTriggerChannel1;
            _LocalSettings.Values["_currentMidiTriggerEvent1"] = "" + (byte)_currentMidiTriggerEvent1;
            _LocalSettings.Values["_debounceActivateInput1"] = "" + _debounceActivateInput1;
            _LocalSettings.Values["_debounceDeactivateInput1"] = "" + _debounceDeactivateInput1;
            _LocalSettings.Values["_displayString1"] = _displayString1;

            _LocalSettings.Values["_currentMidiTriggerChannel2"] = "" + _currentMidiTriggerChannel2;
            _LocalSettings.Values["_currentMidiTriggerEvent2"] = "" + (byte)_currentMidiTriggerEvent2;
            _LocalSettings.Values["_debounceActivateInput2"] = "" + _debounceActivateInput2;
            _LocalSettings.Values["_debounceDeactivateInput2"] = "" + _debounceDeactivateInput2;
            _LocalSettings.Values["_displayString2"] = _displayString2;
        }

        private void LoadSettings()
        {
            try
            {
                _currentMidiTriggerChannel1 = LoadByte("_currentMidiTriggerChannel1");
                _currentMidiTriggerEvent1 = (MidiEventType)LoadByte("_currentMidiTriggerEvent1");
                _debounceActivateInput1 = LoadByte("_debounceActivateInput1");
                _debounceDeactivateInput1 = LoadByte("_debounceDeactivateInput1");
                _displayString1 = LoadString("_displayString1");

                _currentMidiTriggerChannel2 = LoadByte("_currentMidiTriggerChannel2");
                _currentMidiTriggerEvent2 = (MidiEventType)LoadByte("_currentMidiTriggerEvent2");
                _debounceActivateInput2 = LoadByte("_debounceActivateInput2");
                _debounceDeactivateInput2 = LoadByte("_debounceDeactivateInput2");
                _displayString2 = LoadString("_displayString2");
            } catch (FormatException e)
            {
                Log("Saved settings invalid, resetting.");
                ResetSettings();
            }
        }

        private void ResetSettings()
        {
            _LocalSettings.Values["_currentMidiTriggerChannel1"] = "";
            _LocalSettings.Values["_currentMidiTriggerEvent1"] = "";
            _LocalSettings.Values["_debounceActivateInput1"] = "";
            _LocalSettings.Values["_debounceDeactivateInput1"] = "";
            _LocalSettings.Values["_displayString1"] = "";

            _LocalSettings.Values["_currentMidiTriggerChannel2"] = "";
            _LocalSettings.Values["_currentMidiTriggerEvent2"] = "";
            _LocalSettings.Values["_debounceActivateInput2"] = "";
            _LocalSettings.Values["_debounceDeactivateInput2"] = "";
            _LocalSettings.Values["_displayString2"] = "";
        }
    }
}
