﻿using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class XorComponent : AndComponent
    {
        public XorComponent(Item item, XElement element)
            : base(item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            int sendOutput = 0;
            for (int i = 0; i < timeSinceReceived.Length; i++)
            {
                if (timeSinceReceived[i] <= timeFrame) { sendOutput += 1; }
                timeSinceReceived[i] += deltaTime;
            }

            string signalOut = sendOutput == 1 ? output : falseOutput;
            if (string.IsNullOrEmpty(signalOut))
            {
                IsActive = false;
                return;
            }

            item.SendSignal(new Signal(signalOut, sender: signalSender[0] ?? signalSender[1]), "signal_out");
        }
    }
}
