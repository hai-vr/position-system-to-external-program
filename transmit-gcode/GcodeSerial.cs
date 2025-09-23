using System.IO.Ports;
using Hai.PositionSystemToExternalProgram.Core;

namespace Hai.PositionSystemToExternalProgram.Gcode;

public class GcodeSerial
{
    private const int InternalFastestMillimetersPerSecond = 200;
    private const int FastestMillimetersPerMinute = InternalFastestMillimetersPerSecond * 60;
    private const int SlowMillimetersPerMinute = FastestMillimetersPerMinute / 10;
    private const float Retracted = 11.45f;
    private const float BarelyPressed = 12.7f + Retracted - 12.75f;
    private const float FullyPressed = 11.8f + Retracted - 12.75f;
    
    private SerialPort _port;
    private StreamReader _reader;
    private bool _ready;

    private readonly List<EnqueuedCommand> _commandList = new();
    
    private bool _isWaiting;
    private Task<string> _updateAsync;

    public void OpenSerial(string portName)
    {
        if (_port != null)
        {
            _ready = false;
            _port.Close();
            _port = null;
            _reader = null;
        }

        try
        {
            _port = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One);
            _port.Open();
            _reader = new StreamReader(_port.BaseStream);
            _ready = true;
        }
        catch (Exception e)
        {
            _port = null;
            _reader = null;
            _ready = false;
        }
    }

    public void TrySendCoords(RoboticsCoordinates roboticsCoordinates)
    {
        var x01 = (roboticsCoordinates.JoystickTargetL2 + 1) / 2f;
        var y01 = (roboticsCoordinates.JoystickTargetL1 + 1) / 2f;

        if (_commandList.Count > 0)
        {
            // Overwrite the tail of the list if it's already a move instruction
            var lastElement = _commandList.Last();
            if (lastElement.instruction == EnqueuedInstruction.Move)
            {
                _commandList.RemoveAt(_commandList.Count - 1);
            }
            _commandList.Add(new EnqueuedCommand
            {
                instruction = EnqueuedInstruction.Move,
                x01 = x01,
                y01 = y01
            });
        };
    }

    public void CloseSerial()
    {
        if (_port == null) return;
        
        _ready = false;
        var port = _port;
        _port = null;
        _reader = null;
        port.Close();
    }

    public void Update()
    {
        if (_isWaiting)
        {
            if (_updateAsync.IsCompleted)
            {
                if (_updateAsync.Result.EndsWith("ok")) // FIXME: Should this be endWith
                {
                    _isWaiting = false;
                    _updateAsync = null;
                }
                else
                {
                    _updateAsync = UpdateAsync();
                }
            }
        }

        // The state of _isWaiting could have changed as a result of receiving "ok" above.
        if (_isWaiting) return;
        
        if (_commandList.Count > 0)
        {
            var instruction = _commandList[0];
            _commandList.RemoveAt(0);
            if (instruction.instruction == EnqueuedInstruction.Move)
            {
                DrawTo(instruction.x01, instruction.y01);
                _isWaiting = true;
                _updateAsync = UpdateAsync();
            }
        }
    }

    private async Task<string> UpdateAsync()
    {

        return await _reader.ReadLineAsync();
    }

    
    public void AutoHomeXYOnly()
    {
        SafeWriteLine("G28 X Y");
    }

    public void AsFastAsPossible()
    {
        SafeWriteLine($"G1 F{FastestMillimetersPerMinute}");
    }

    public void Slowly()
    {
        SafeWriteLine($"G1 F{SlowMillimetersPerMinute}");
    }

    private void DrawTo(float x01, float y01)
    {
        var sq = 145;
        var i = 65;
        MoveTo(i + x01 * sq, i + y01 * sq);
    }

    private void MoveTo(float xMillimeters, float yMillimeters)
    {
        if (IsFloatIllegalRange(xMillimeters) || IsFloatIllegalRange(yMillimeters))
        {
            return;
        }
        SafeWriteLine($"G1 X{xMillimeters:0.000} Y{yMillimeters:0.000}");
    }

    private bool IsFloatIllegalRange(float value)
    {
        if (IsFloatInvalid(value)) return true;
        
        return value < 0 || value > 210;
    }

    private bool IsFloatInvalid(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value);
    }

    private void SafeWriteLine(string message)
    {
        try
        {
            _port.WriteLine(message);
            WaitForOkResponse();
        }
        catch (Exception e)
        {
            _ready = false;
            Console.WriteLine($"Exception returned, will close and abandon port: {e.Message}");
            var port = _port;
            _port = null;
            _reader = null;
            port.Close();
        }
    }

    private void WaitForOkResponse()
    {
        string readLine;
        do
        {
            readLine = _port.ReadLine();
            Console.WriteLine($"Read line: {readLine}");
        } while (!readLine.EndsWith("ok"));
    }

    private class EnqueuedCommand
    {
        public EnqueuedInstruction instruction;
        public float x01;
        public float y01;
    }

    private enum EnqueuedInstruction
    {
        Move
    }
}