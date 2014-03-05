﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;

namespace Simulation
{
    enum Type { MACHINE_1, MACHINE_2, MACHINE_3, ADD_TO_CRATE, MACHINE_4, BREAKDOWN_1, REPAIRED_1, REPAIRED_4, END_OF_SIMULATION };

    public enum State { IDLE, BUSY, BLOCKED, BBROKEN, BROKEN, WASBROKEN };
    public enum Machine { M1a, M1b, M1c, M1d, M2a, M2b, M3a, M3b, M4a, M4b, DUMMY };

    public class Simulation
    {
        private IUpdate parent;

        private static int RUN_LENGTH = 10000;
        private TreeSet<Event> eventList;
        public bool Paused { get; set; }            // is the simulation paused
        public bool Running { get; set; }   // is the simulation running
        public int Speed { get; set; }   // Simulation speed

        private Random random;

        private int CRATE_SIZE = 20;
        private int BUFFER_SIZE = 20;

        // State variables
        public float Time { get; private set; }  // Simulation time
        public Dictionary<Machine, State> MachineState { get; private set; }    // state of each individual machine
        public int BufferA { get; private set; }     // buffer between machine 1a, machine 1b and machine 2a
        public int BufferB { get; private set; }     // buffer between machine 1c, machine 1d and machine 2b


        // public int cratesReadyforInputM3 { get; private set; }       // ?
        public int dvdReadyForM3 { get; private set; }               // number of dvd in a crate ready to be prossed by machine 3
        public int cratesToBeFilledM3 { get; private set; }         // number of crates ready to be filled before machine 3 can process a crate;

        public int dvdReadyForInputM4 { get; private set; }         // number of dvd ready to be processed by machine 4, 
        //   at least one dvd ready means there is a crate, 21 dvd ready means that there are two crates 

        public int dvdCounter { get; private set; }         // counter for how many dvds the production has started to produce
        public int dvdProduced { get; private set; }        // number of DVD produced 
        public int dvdFailed { get; private set; }          // number of DVD that failed during the process
        public int dvdInProduction { get; private set; }    // number of DVD in production

        // state of the machine in case something has gone wrong
        private Dictionary<Machine, float> TimeM1ShouldHaveFinished;
        private Dictionary<Machine, float> TimeM1HasBrokenDown;
        private Dictionary<Machine, int> dvdBeforeM4Service;

        public Simulation(IUpdate parent)
        {
            this.parent = parent;
            Initialize();
        }

        public void Initialize()
        {
            eventList = new TreeSet<Event>();
            random = new Random();

            // add initial events, machine 1, breakdowns 1,3,4, and end of simulation
            BufferA = BufferB = 0;
            cratesToBeFilledM3 = 6;
            dvdReadyForM3 = 0;
            dvdReadyForInputM4 = 0;

            dvdCounter = 1;
            dvdProduced = 0;
            dvdFailed = 0;
            dvdInProduction = 0;

            TimeM1ShouldHaveFinished = new Dictionary<Machine, float>();
            TimeM1HasBrokenDown = new Dictionary<Machine, float>();
            dvdBeforeM4Service = new Dictionary<Machine, int>();
            dvdBeforeM4Service[Machine.M4a] = M4Service();
            dvdBeforeM4Service[Machine.M4b] = M4Service();

            MachineState = new Dictionary<Machine, State>();
            MachineState[Machine.M1a] = State.BUSY;
            MachineState[Machine.M1b] = State.BUSY;
            MachineState[Machine.M1c] = State.BUSY;
            MachineState[Machine.M1d] = State.BUSY;
            MachineState[Machine.M2a] = State.IDLE;
            MachineState[Machine.M2b] = State.IDLE;
            MachineState[Machine.M3a] = State.IDLE;
            MachineState[Machine.M3b] = State.IDLE;
            MachineState[Machine.M4a] = State.IDLE;
            MachineState[Machine.M4b] = State.IDLE;

            scheduleM1(0, Machine.M1a);
            scheduleM1(0, Machine.M1b);
            scheduleM1(0, Machine.M1c);
            scheduleM1(0, Machine.M1d);

            scheduleM1Breakdown(0, Machine.M1a);
            scheduleM1Breakdown(0, Machine.M1b);
            scheduleM1Breakdown(0, Machine.M1c);
            scheduleM1Breakdown(0, Machine.M1d);

            eventList.Add(new Event(RUN_LENGTH, Type.END_OF_SIMULATION, Machine.DUMMY, 0));

            Time = 0;
            Running = false;
            Paused = false;
            Speed = 9;
        }

        public void Run()
        {
            Running = true;
            Console.WriteLine("Simulation running");
            while (Running)
            {
                if (!Paused)
                {
                    Event e = eventList.Pop();
                    Time = e.Time;

                    Console.WriteLine("Process Event: " + e);

                    switch (e.Type)
                    {
                        case Type.MACHINE_1:
                            M1Finished(e);
                            break;
                        case Type.MACHINE_2:
                            M2Finished(e);
                            break;
                        case Type.ADD_TO_CRATE:
                            AddDVDToCrate(e);
                            break;
                        case Type.MACHINE_3:
                            M3Finished(e);
                            break;
                        case Type.MACHINE_4:
                            M4Finished(e);
                            break;
                        case Type.BREAKDOWN_1:
                            BreakdownM1(e);
                            break;
                        case Type.REPAIRED_1:
                            RepairM1(e);
                            break;
                        case Type.REPAIRED_4:
                            RepairM4(e);
                            break;
                        default:
                            Console.WriteLine("FAIL!");
                            break;
                    }

                    parent.UpdateSim();
                }
                Thread.Sleep(Speed);
            }
            Console.WriteLine("Simulation finished");
        }

        private void M1Finished(Event e)
        {
            float time = e.Time;
            Machine machine = e.Machine;

            State state = MachineState[machine];
            if (state == State.BROKEN)
            {
                // M1 is broken which cause the dvd that was supposed to be finished to be still in M1
                 TimeM1ShouldHaveFinished[machine] = time;
            }
            else if (state == State.WASBROKEN)
            {
                // M1 has been broken during producing the dvd
                // the repair time of the machine is being added to the finishing time of the dvd
                MachineState[machine] = State.BUSY;

                // schedule M1Finished at time machine 1 was broken 
                eventList.Add(new Event(time + (time - TimeM1HasBrokenDown[machine]), e.Type, machine, e.DVD));
                
                TimeM1HasBrokenDown.Remove(machine);
                TimeM1ShouldHaveFinished.Remove(machine);
            }
            else if (machine == Machine.M1a || machine == Machine.M1b)
            {
                // keep producing dvd's, schedule new M1Finished
                BufferA++;
                scheduleM1(time, machine);

                if (MachineState[Machine.M2a] == State.IDLE)
                {
                    // Make M2a start production if M2a is available
                    MachineState[Machine.M2a] = State.BUSY;

                    // schedule the next step of the dvd production
                    scheduleM2(time, Machine.M2a);
                }
            }
            else
            {
                // M2b isn't available for input, place the DVD in buffer if there is room
                BufferB++;
                // keep producing dvd's, schedule new M1Finished
                scheduleM1(time, machine);

                if (MachineState[Machine.M2b] == State.IDLE)
                {
                    // Make M2b start production if M2b is available
                    MachineState[Machine.M2b] = State.BUSY;

                    // schedule the next step of the dvd production and restart machine to produce the next dvd
                    scheduleM2(time, Machine.M2b);
                }
            }
        }

        private void M2Finished(Event e)
        {
            // schedule AddDVDtoCrate event 
            scheduleAddDVDToCrate(e.Time);

            if (e.Machine == Machine.M2a)
            {
                // schedule M2Finished
                scheduleM2(e.Time, e.Machine);

                // check if machine was blocked and need to be scheduled again
                if (MachineState[Machine.M1a] == State.BLOCKED)
                {
                    MachineState[Machine.M1a] = State.BUSY;
                    scheduleM1(e.Time, Machine.M1a);
                }
                if (MachineState[Machine.M1b] == State.BLOCKED)
                {
                    MachineState[Machine.M1b] = State.BUSY;
                    scheduleM1(e.Time, Machine.M1b);
                }
            }
            else
            {       // schedule M2Finished
                scheduleM2(e.Time, e.Machine);

                if (MachineState[Machine.M1c] == State.BLOCKED)
                {
                    MachineState[Machine.M1c] = State.BUSY;
                    scheduleM1(e.Time, Machine.M1c);
                }
                if (MachineState[Machine.M1d] == State.BLOCKED)
                {
                    MachineState[Machine.M1d] = State.BUSY;
                    scheduleM1(e.Time, Machine.M1d);
                }
            }
        }

        private void AddDVDToCrate(Event e)
        {
            dvdReadyForM3++;

            // Check if a crate is full and therefore ready to be put in machine 3
            if (dvdReadyForM3 >= CRATE_SIZE)
            {
                // If M3 is available we start it's production
                if (MachineState[Machine.M3a] == State.IDLE)
                {
                    MachineState[Machine.M3a] = State.BUSY;
                    scheduleM3(e.Time, Machine.M3a);
                }
                else if (MachineState[Machine.M3b] == State.IDLE)
                {
                    MachineState[Machine.M3b] = State.BUSY;
                    scheduleM3(e.Time, Machine.M3b);
                }
            }
        }

        private void M3Finished(Event e)
        {
            dvdReadyForInputM4 += CRATE_SIZE;
            // M3 is finished and starts M4 if M4 is available
            if (MachineState[Machine.M4a] == State.IDLE)
            {
                MachineState[Machine.M4a] = State.BUSY;
                scheduleM4(e.Time, Machine.M4a);
            }

            if (MachineState[Machine.M4b] == State.IDLE)
            {
                MachineState[Machine.M4b] = State.BUSY;
                scheduleM4(e.Time, Machine.M4b);
            }
            scheduleM3(e.Time, e.Machine);
        }

        private void M4Finished(Event e)
        {
            // update statistics

            // The change the dvd has failed during the production
            double change = random.NextDouble();
            if (change > 0.02) // 2% van de dvds
            {
                dvdProduced++;
            }
            else
            {
                dvdFailed++;
            }
            dvdInProduction--;
            dvdBeforeM4Service[e.Machine]--;


            int before = dvdReadyForInputM4;

            scheduleM4(e.Time, e.Machine);

            // If M4 emptied a whole crate
            if (before % CRATE_SIZE == 1 && dvdReadyForInputM4 % CRATE_SIZE == 0)
            {
                // a crate is empty and ready to be filled again
                cratesToBeFilledM3++;

                // If M2 was blocked in it's output, lift the blockade, there are empty crates again
                if (MachineState[Machine.M2a] == State.BLOCKED)
                {
                    MachineState[Machine.M2a] = State.BUSY;
                    scheduleM2(e.Time, Machine.M2a);
                }

                if (MachineState[Machine.M2b] == State.BLOCKED)
                {
                    MachineState[Machine.M2b] = State.BUSY;
                    scheduleM2(e.Time, Machine.M2b);
                }
            }
        }

        private void BreakdownM1(Event e)
        {
            MachineState[e.Machine] = (MachineState[e.Machine] == State.BLOCKED ? State.BBROKEN : State.BROKEN);
            TimeM1HasBrokenDown[e.Machine] = e.Time;

            // schedule repair machine 1: 2 hours exp distr.
            scheduleM1Repair(e.Time, e.Machine);
        }

        private void RepairM1(Event e)
        {
            // schedule breakdown machine 1: 
            scheduleM1Breakdown(e.Time, e.Machine);

            // check if the machine was in the meantime finished
            if (TimeM1ShouldHaveFinished.ContainsKey(e.Machine))
            {
                // schedule machine 1 finished: time now (= de tijd dat hij gerepareerd is) + time product should have finished - time broken down
                float delay = TimeM1ShouldHaveFinished[e.Machine] - TimeM1HasBrokenDown[e.Machine];

                //repair time 2 hours exp. 
                MachineState[e.Machine] = State.BUSY;
                eventList.Add(new Event(e.Time + delay, Type.MACHINE_1, e.Machine, 0));

                // reset variables
                TimeM1HasBrokenDown.Remove(e.Machine);
                TimeM1ShouldHaveFinished.Remove(e.Machine);
            }
            else if (MachineState[e.Machine] == State.BBROKEN)
            {
                MachineState[e.Machine] = State.BUSY;
                scheduleM1(e.Time, e.Machine);
            }
            else 
            {
                // het is nog niet bekend hoe lang de dvd in de machine heeft gezeten als die kapot is geweest
                MachineState[e.Machine] = State.WASBROKEN;
            }
        }

        private void RepairM4(Event e)
        {
            dvdBeforeM4Service[e.Machine] = M4Service();
            MachineState[e.Machine] = State.BUSY;
            scheduleM4(e.Time, e.Machine);
        }

        private void scheduleM1(float time, Machine machine)
        {
            int limit, buffer = BUFFER_SIZE;

            // the limit of the buffer is 19 when the other machine will produce the 20th dvd.
            if (machine == Machine.M1a || machine == Machine.M1b)
            {
                buffer = BufferA;
                limit = (MachineState[Machine.M1a] == State.BUSY && MachineState[Machine.M1b] == State.BUSY ? BUFFER_SIZE - 1 : BUFFER_SIZE);
            }
            else
            {
                buffer = BufferB;
                limit = (MachineState[Machine.M1c] == State.BUSY && MachineState[Machine.M1d] == State.BUSY ? BUFFER_SIZE - 1 : BUFFER_SIZE);
            }


            if (buffer >= limit)
            {
                // stop production, buffer full
                MachineState[machine] = State.BLOCKED;
            }
            else
            {
                // keep producing dvd's, schedule new M1Finished
                dvdInProduction++;
                float processTime = 59.6f; // gemiddelde
                eventList.Add(new Event(time + processTime, Type.MACHINE_1, machine, 0));
            }
        }
        private void scheduleM2(float time, Machine machine)
        {
            int limit = cratesToBeFilledM3 * CRATE_SIZE - (MachineState[Machine.M2a] == State.BUSY && MachineState[Machine.M2b] == State.BUSY ? 0 : 1);
            if (dvdReadyForM3 <= limit && cratesToBeFilledM3 > 0)
            {
                if (machine == Machine.M2a)
                {
                    if (BufferA > 0)
                    {
                        BufferA--;

                        float processTime = 24f; // 
                        eventList.Add(new Event(time + processTime, Type.MACHINE_2, machine, 0));
                    }
                    else
                    {
                        // no input for the machine
                        MachineState[machine] = State.IDLE;
                    }
                }
                else
                {
                    if (BufferB > 0)
                    {
                        BufferB--;

                        float processTime = 24f; // 
                        eventList.Add(new Event(time + processTime, Type.MACHINE_2, machine, 0));
                    }
                    else
                    {
                        // no input for the machine
                        MachineState[machine] = State.IDLE;
                    }
                }
            }
            else
            {
                // the machine will not be able to output the next dvd
                MachineState[machine] = State.BLOCKED;
            }
        }
        private void scheduleAddDVDToCrate(float time)
        {
            float processTime = 5 * 60f; // 
            eventList.Add(new Event(time + processTime, Type.ADD_TO_CRATE, Machine.DUMMY, 0));
        }
        private void scheduleM3(float time, Machine machine)
        {
            // If a full crate is available for input M3, start producing this crate. Else, output the crate and go back to waiting for input. 
            if (dvdReadyForM3 >= CRATE_SIZE && cratesToBeFilledM3 > 0)
            {
                cratesToBeFilledM3--;
                dvdReadyForM3 -= CRATE_SIZE;

                float processTime = 10 + 6 + 3 * 60f; // 
                eventList.Add(new Event(time + processTime, Type.MACHINE_3, machine, 0));
            }
            else
            {
                MachineState[machine] = State.IDLE;
            }
        }
        private void scheduleM4(float time, Machine machine)
        {
            if (dvdBeforeM4Service[machine] == 0)
            {
                MachineState[machine] = State.BROKEN;
                scheduleM4Repair(time, machine);
            }
            else if (dvdReadyForInputM4 > 0)
            {
                dvdReadyForInputM4--;
                float processTime = 25f; // gemiddelde
                eventList.Add(new Event(time + processTime, Type.MACHINE_4, machine, 0));
            }
            else
            {
                MachineState[machine] = State.IDLE;
            }
        }

        private void scheduleM1Breakdown(float time, Machine machine)
        {
            float breakTime = 8 * 60f;   // 8 hours exp distr
            eventList.Add(new Event(time + breakTime, Type.BREAKDOWN_1, machine, 0));
        }
        private void scheduleM1Repair(float time, Machine machine)
        {
            float breakTime = 2 * 60 * 60f;   // 2 hours exp distr
            eventList.Add(new Event(time + breakTime, Type.REPAIRED_1, machine, 0));
        }
        private void scheduleM4Repair(float time, Machine machine)
        {
            float breakTime = 15 * 60f;   // 15 min exp distr
            eventList.Add(new Event(time + breakTime, Type.REPAIRED_4, machine, 0));
        }

        private int M4Service()
        {
            return 200;
        }   
    }
}
