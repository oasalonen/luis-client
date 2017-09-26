using Microsoft.Azure.Devices;
using Microsoft.Cognitive.LUIS;
using Microsoft.CognitiveServices.SpeechRecognition;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LuisClient
{
    public partial class MainWindow : Window
    {
        private static readonly string LuisAppIdEn = "";
        private static readonly string LuisAppIdDe = "";
        private static readonly string LuisAppKey = "";
        private static readonly string BingSpeechAppKey = "";
        private static readonly string IotConnectionString = "";

        public class IntentPayload
        {
            public string intent { get; set; }
            public double score { get; set; }
        }

        public class EntityPayload
        {
            public string entity { get; set; }
            public string type { get; set; }
            public double score { get; set; }
        }

        public class ResponsePayload
        {
            public string query { get; set; }
            public IntentPayload[] intents { get; set; }
            public EntityPayload[] entities { get; set; }
        }

        public string Locale
        {
            get
            {
                return _locale;
            }
            set
            {
                _locale = value;
                string luisApp;
                if (_locale == "en-US")
                {
                    luisApp = LuisAppIdEn;
                }
                else
                {
                    luisApp = LuisAppIdDe;
                }
                _luisClient = new Microsoft.Cognitive.LUIS.LuisClient(luisApp, LuisAppKey, true, "westus");
                initMicClient(_locale, luisApp);
            }
        }
        private string _locale = "en-US";

        private Microsoft.Cognitive.LUIS.LuisClient _luisClient;

        private MicrophoneRecognitionClientWithIntent _micClient;
        private CompositeDisposable _micSubscriptions = new CompositeDisposable();
        private ServiceClient _iotClient = ServiceClient.CreateFromConnectionString(IotConnectionString);

        public MainWindow()
        {
            InitializeComponent();

            Locale = "en-US";
            english.IsChecked = true;

            // See https://www.luis.ai/applications
            // Make pi run the following command: python app.py 'CONNECTION_STRING'

            var enterKeyStream = Observable.FromEventPattern<KeyEventArgs>(command, "KeyDown")
                .Select(e => ((KeyEventArgs)e.EventArgs).Key)
                .Where(k => k == Key.Enter)
                .Select(k => (Object)null);

            Observable.FromEventPattern(submit, "Click")
                .Merge(enterKeyStream)
                .ObserveOnDispatcher()
                .Select(_ =>
                {
                    var text = command.Text;
                    responseText.Text += "Recognized command: " + text;

                    command.Text = "";
                    responseText.Text = "";
                    return text;
                })
                .Where(text => !String.IsNullOrWhiteSpace(text))
                .ObserveOnDispatcher()
                .SelectMany(text =>
                {
                    responseText.Text += "Analyzing text... ";
                    Debug.WriteLine("Predicting: " + text);
                    return _luisClient.Predict(text).ToObservable();
                })
                .ObserveOnDispatcher()
                .SelectMany(result =>
                {
                    responseText.Text += "Done";
                    responseText.Text += "\nIntent: " + result.TopScoringIntent.Name;
                    Debug.WriteLine(result.TopScoringIntent.Name + ": " + result.TopScoringIntent.Score);

                    bool isLight = result.Entities.ContainsKey("light");
                    responseText.Text += "\nEntity: " + (isLight ? "light" : "unknown");

                    if (isLight)
                    {
                        responseText.Text += "\nSending command... ";
                        return SendIntentAsync(_iotClient, result.TopScoringIntent.Name, "light", parseFrequency(result)).ToObservable();
                    }
                    else
                    {
                        responseText.Text += "\nNO ENTITIES FOUND. No action required. ";
                        return Task.Delay(TimeSpan.FromMilliseconds(0)).ToObservable();
                    }
                })
                .ObserveOnDispatcher()
                .Subscribe(result =>
                {
                    responseText.Text += "Done";
                });
        }

        private int parseFrequency(LuisResult result)
        {
            int value = 2;
            try
            {
                Int32.TryParse(result.CompositeEntities["frequency"].First().CompositeEntityChildren.First().Value, out value);
                return value;
            }
            catch
            {
                return value;
            }
        }

        private int parseFrequency(ResponsePayload payload)
        {
            int value = 2;
            try
            {
                Int32.TryParse(payload.entities.First(e => e.type == "builtin.number").entity, out value);
                return value;
            }
            catch
            {
                return value;
            }
        }

        private void initMicClient(string locale, string luisApp)
        {
            _micSubscriptions.Clear();

            _micClient = SpeechRecognitionServiceFactory.CreateMicrophoneClientWithIntent(locale, BingSpeechAppKey, luisApp, LuisAppKey);

            var sub1 = Observable.FromEventPattern(record, "Checked")
                .Subscribe(_ =>
                {
                    responseText.Text = "Recording!";
                    Debug.WriteLine("Start speech");
                    _micClient.StartMicAndRecognition();
                });
            var sub2 = Observable.FromEventPattern(record, "Unchecked")
                .Subscribe(_ =>
                {
                    responseText.Text += "\nStopped recording";
                    Debug.WriteLine("Stop speech");
                    _micClient.EndMicAndRecognition();
                });

            var sub3 = Observable.FromEventPattern<SpeechIntentEventArgs>(_micClient, "OnIntent")
                .Select(e => e.EventArgs as SpeechIntentEventArgs)
                .ObserveOnDispatcher()
                .SelectMany(args =>
                {
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(args.Payload)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(ResponsePayload));
                        var payload = serializer.ReadObject(ms) as ResponsePayload;
                        responseText.Text += "\nDetected speech: " + payload.query;
                        if (payload.intents.Length > 0)
                        {
                            responseText.Text += "\nIntent: " + payload.intents[0].intent + " (" + payload.intents[0].score + ")";
                            bool isLight = payload.entities.FirstOrDefault(e => e.type == "light") != null;
                            responseText.Text += "\nEntity: " + (isLight ? "light" : "unknown");
                            if (isLight)
                            {
                                responseText.Text += "\nSending command... ";
                                return SendIntentAsync(_iotClient, payload.intents[0].intent, "light", parseFrequency(payload)).ToObservable();
                            }
                            else
                            {
                                responseText.Text += "\nNO ENTITIES FOUND. No action required. ";
                                return Task.Delay(TimeSpan.FromMilliseconds(0)).ToObservable();
                            }
                        }
                        else
                        {
                            responseText.Text += "\nNO INTENTS FOUND. No action required. ";
                            return Task.Delay(TimeSpan.FromMilliseconds(0)).ToObservable();
                        }
                    }
                })
                .ObserveOnDispatcher()
                .Subscribe(_ =>
                {
                    responseText.Text += "Done";
                });

            _micSubscriptions.Add(sub1);
            _micSubscriptions.Add(sub2);
            _micSubscriptions.Add(sub3);
        }

        private async Task SendIntentAsync(ServiceClient iotClient, string intent, string entity, int frequency)
        {
            if (intent != null && entity != null)
            {
                var message = new Message(Encoding.UTF8.GetBytes("{\"intent\": \"" + intent + "\", \"entity\": \"" + entity + "\", \"frequency\": " + frequency + "}"));
                await iotClient.SendAsync("raspberry", message).ToObservable();
            }
            else
            {
                Debug.WriteLine("NO INTENT OR ENTITY");
            }
        }

        private void german_Click(object sender, RoutedEventArgs e)
        {
            Locale = "de-DE";
        }

        private void english_Click(object sender, RoutedEventArgs e)
        {
            Locale = "en-US";
        }
    }
}
