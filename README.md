# luis-client
A test client for [Azure LUIS](https://www.luis.ai/). Deciphers intent from text and speech, and uses the information to control a raspberry pi LED through Azure IoT Hub.

## Setup
* Azure LUIS app for English (optionally also for German)
  * Entities: light, frequency (number + Hz)
  * Intents: turn on light, turn off light, blink light at frequency
* Bing Speech API
* Azure IoT Hub
* Raspberry PI connected to Azure IoT Hub that controls an LED

Add all the required app IDs, app keys, and connection strings in ```MainWindow.xaml.cs```.
