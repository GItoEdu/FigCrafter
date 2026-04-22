using System;
using System.Collections.Generic;
using FigCrafterApp.Models;
using FigCrafterApp.ViewModels;

namespace FigCrafterApp.Commands
{
    public class RotateCanvasCommand : IUndoableCommand
    {
        private class ObjectState
        {
            public GraphicObject Obj { get; set; } = null!;
            public float X { get; set; }
            public float Y { get; set; }
            public float Rotation { get; set; }
            public float EndX { get; set; }
            public float EndY { get; set; }
        }

        private readonly List<ObjectState> _oldStates = new();
        private readonly List<ObjectState> _newStates = new();

        public RotateCanvasCommand(CanvasViewModel vm, float angleDeg, float cx, float cy)
        {
            float angleRad = angleDeg * (float)Math.PI / 180.0f;
            float cos = (float)Math.Cos(angleRad);
            float sin = (float)Math.Sin(angleRad);

            // 再帰的にすべてのオブジェクトの状態を記録・計算するローカル関数
            void ProcessObject(GraphicObject obj)
            {
                // 古い状態を保存
                var oldState = new ObjectState
                {
                    Obj = obj,
                    X = obj.X,
                    Y = obj.Y,
                    Rotation = obj.Rotation
                };
                if (obj is LineObject oldLine)
                {
                    oldState.EndX = oldLine.EndX;
                    oldState.EndY = oldLine.EndY;
                }
                _oldStates.Add(oldState);

                // 中心(cx, cy)を軸にした新しい座標を計算
                float dx = obj.X - cx;
                float dy = obj.Y - cy;
                float newX = cx + dx * cos - dy * sin;
                float newY = cy + dx * sin + dy * cos;

                var newState = new ObjectState
                {
                    Obj = obj,
                    X = newX,
                    Y = newY,
                    // 角度を足して360度の範囲に収める
                    Rotation = (obj.Rotation + angleDeg + 360.0f) % 360.0f
                };

                // LineObjectの場合は終点も回転させる
                if (obj is LineObject line)
                {
                    float edx = line.EndX - cx;
                    float edy = line.EndY - cy;
                    newState.EndX = cx + edx * cos - edy * sin;
                    newState.EndY = cy + edx * sin + edy * cos;
                }
                _newStates.Add(newState);

                // グループの場合は子オブジェクトも処理
                if (obj is GroupObject group)
                {
                    foreach (var child in group.Children)
                    {
                        ProcessObject(child);
                    }
                }
            }

            // 全レイヤーの全オブジェクトに対して処理を実行
            foreach (var layer in vm.Layers)
            {
                foreach (var obj in layer.GraphicObjects)
                {
                    ProcessObject(obj);
                }
            }
        }

        public void Execute()
        {
            foreach (var state in _newStates)
            {
                ApplyState(state);
            }
        }

        public void Undo()
        {
            foreach (var state in _oldStates)
            {
                ApplyState(state);
            }
        }

        private void ApplyState(ObjectState state)
        {
            state.Obj.X = state.X;
            state.Obj.Y = state.Y;
            state.Obj.Rotation = state.Rotation;
            if (state.Obj is LineObject line)
            {
                line.EndX = state.EndX;
                line.EndY = state.EndY;
            }
        }
    }
}