[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]
[![NuGet][nuget-shield]][nuget-url]



<br />
<div align="center">
  <a href="https://github.com/fio-fsharp/fio">
    <img src="assets/images/fio_logo_wide.png" width="auto" height="300" alt="FIO Logo">
  </a>

  <p align="center">
    <br />
    🪻 A type-safe, highly concurrent and asynchronous library for F# based on pure functional programming
    <br />
  </p>
</div>



## Table of Contents
- [Introduction](#introduction)
- [Built With](#built-with)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Benchmarks](#benchmarks)
- [Performance](#performance)
- [License](#license)
- [Contact](#contact)
- [Acknowledgments](#acknowledgments)



## Introduction
**FIO** is a type-safe, highly concurrent and asynchronous library for the [**F#**](https://fsharp.org/) programming language. Based on pure functional programming principles, it serves as an embedded [**domain-specific language (DSL)**](https://martinfowler.com/dsl.html) empowering developers to craft type-safe, concurrent and maintainable programs with ease using functional effects.

Harnessing concepts from functional programming, **FIO** simplifies the creation of scalable and efficient concurrent applications. It introduces the **IO monad** to manage expressions with side effects and employs “green threads” (also known as fibers) for scalable and efficient concurrency. **FIO** aims to provide an environment similar to that of [**ZIO**](https://zio.dev/), drawing inspiration from both [**ZIO**](https://zio.dev/) and [**Cats Effect**](https://typelevel.org/cats-effect/).

**FIO** was initially developed as part of a master's thesis in Computer Science and Engineering at the [**Technical University of Denmark (DTU)**](https://www.dtu.dk/english/). You can read the thesis, which provides more details about **FIO**, [**here**](https://iyyel.io/assets/doc/masters_thesis_daniel_larsen.pdf). Some parts may - however - be outdated as development continues.

**DISCLAIMER:** **FIO** is in early development stages and a lot of improvements and enhancements can be made. If you think the project sounds interesting, do not hesitate to create a PR or contact me for further information or assistance.



## Built With
**FIO** is built using the following technologies:

* [**F#**](https://fsharp.org/)
* [**.NET**](https://dotnet.microsoft.com/en-us/)



## Getting Started
It is easy to get started with **FIO**.

* Download and install [**.NET**](https://dotnet.microsoft.com/en-us/)
* Download and install a compatible IDE such as [**Visual Studio**](https://visualstudio.microsoft.com/downloads/) or [**Rider**](https://www.jetbrains.com/rider/download/), or a text editor like [**Visual Studio Code**](https://code.visualstudio.com/).

* Download or clone this repository
* Open it in your IDE or text editor of choice
* Navigate to the [**FIO.Examples**](https://github.com/fio-fsharp/fio/tree/dev/src/FIO.Examples) project and check out the example programs or create a new F# file to start using **FIO**



## Usage

There are currently two ways of using **FIO**. It is possible to create effects directly and execute them using one of **FIO**'s runtime systems. This gives the developer more control over the runtime and how the effect is executed. In addition, the core **FIO** library provides a **FIOApp** type which encapsulates elements such as the runtime which may not be important for the developer.

### Direct usage of effects
Create a new F# file and import the library using ```open FIO.Core``` in either the cloned repository or a project with the **FIO** NuGet package installed. To use **FIO**'s advanced runtime, add ```open FIO.Runtime.Advanced``` as well. For example:

```fsharp
module DirectUsage

open System

open FIO.Core
open FIO.Runtime.Advanced

[<EntryPoint>]
let main _ =
    let askForName = fio {
        do! !+ printfn("Hello! What is your name?")
        let! name = !+ Console.ReadLine()
        do! !+ printfn($"Hello, %s{name}, welcome to FIO! 🪻💜")
    }

    let fiber = AdvancedRuntime().Run askForName
    let result = fiber.AwaitResult()
    printfn $"%A{result}"
    exit 0
```

You can then execute the program with

```$ dotnet run```

and you'll see

```
Hello! What is your name?
Daniel
Hello, Daniel, welcome to FIO! 🪻💜
Ok ()
```

### FIOApp usage

In general it is recommended to create a type extending the **FIOApp** type. A **FIOApp** is essentially a wrapper around the effect which hides elements such as the runtime system and makes it possible to write cleaner **FIO** programs. For example:

```fsharp
module FIOAppUsage

open System

open FIO.Core

type WelcomeApp() =
    inherit FIOApp<unit, obj>()

    override this.effect = fio {
        do! !+ printfn("Hello! What is your name?")
        let! name = !+ Console.ReadLine()
        do! !+ printfn($"Hello, %s{name}, welcome to FIO! 🪻💜")
    }
  
WelcomeApp().Run()
```

Once again, you can execute the **FIOApp** using

```$ dotnet run```

and you'll see the same result as before

```
Hello! What is your name?
Daniel
Hello, Daniel, welcome to FIO! 🪻💜
Ok ()
```

**Side note:** It is also possible to avoid using the **FIO** computation expression and instead directly use the **FIO** DSL. The above example would then look like this:

```fsharp
let askForName =
    !+ printfn("Hello! What is your name?") >>= fun _ ->
    !+ Console.ReadLine() >>= fun name ->
    !+ printfn($"Hello, %s{name}, welcome to FIO! 🪻💜")
```

where ```>>=``` is **FIO**'s bind function.



## Benchmarks
This repository contains five benchmarks that each test an aspect of concurrent computing.
All benchmarks reside from the [**Savina - An Actor Benchmark Suite**](http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf) paper.

* Pingpong (Message sending and retrieval)
* ThreadRing (Message sending and retrieval, context switching between fibers)
* Big (Contention on channel, many-to-many message passing)
* Bang (Many-to-one messaging)
* Fork (Spawning time of fibers)

The benchmarks can be given the following command line options:

```
USAGE: FIO.Benchmarks [--help] [--naive-runtime] [--intermediate-runtime <evalworkers> <blockingworkers> <evalsteps>]
                      [--advanced-runtime <evalworkers> <blockingworkers> <evalsteps>]
                      [--deadlocking-runtime <evalworkers> <blockingworkers> <evalsteps>] --runs <runs>
                      [--process-increment <actor inc> <inc times>] [--pingpong <rounds>]
                      [--threadring <actors> <rounds>] [--big <actors> <rounds>] [--bang <actors> <rounds>]
                      [--fork <actors>]

OPTIONS:

    --naive-runtime       specify naive runtime.
    --intermediate-runtime <evalworkers> <blockingworkers> <evalsteps>
                          specify evaluation workers, blocking workers and eval steps for intermediate runtime.
    --advanced-runtime <evalworkers> <blockingworkers> <evalsteps>
                          specify evaluation workers, blocking workers and eval steps for advanced runtime.
    --deadlocking-runtime <evalworkers> <blockingworkers> <evalsteps>
                          specify evaluation workers, blocking workers and eval steps for deadlocking runtime.
    --runs <runs>         specify the number of runs for each benchmark.
    --process-increment <actor inc> <inc times>
                          specify the value of actor increment and how many times.
    --pingpong <rounds>   specify rounds for pingpong benchmark.
    --threadring <actors> <rounds>
                          specify actors and rounds for threadring benchmark.
    --big <actors> <rounds>
                          specify actors and rounds for big benchmark.
    --bang <actors> <rounds>
                          specify actors and rounds for bang benchmark.
    --fork <actors>       specify actors for fork benchmark.
    --help                display this list of options.
```

For example, running 30 runs of each benchmark using the advanced runtime with 7 evaluation workers, 1 blocking worker and 15 evaluation steps would look as so:

```
--advanced-runtime 7 1 15 --runs 30 --pingpong 120000 --threadring 2000 1 --big 500 1 --bang 3000 1 --fork 3000
```

Additionally, **FIO** supports two conditional compilation options:

* **DETECT_DEADLOCK:** Enables a naive deadlock detecting thread that attempts to detect if a deadlock has occurred when running **FIO** programs
* **MONITOR:** Enables a monitoring thread that prints out data structure content during when running **FIO** programs

**DISCLAIMER:** These features are very experimental and may not work as intended.



## Performance
Below the scalability of each runtime system can be seen for each benchmark. **I** is denoting the intermediate runtime and **A** the advanced. To give some insight into the runtimes, the naive runtime uses operating system threads, the intermediate uses fibers with handling of blocked fibers in linear time, and the advanced uses fibers with constant time handling.

#### **Threadring**
<img src="assets/images/threadring_scalability_plot.png" width="auto" height="500" alt="Threadring scalability plot">
 
#### **Big**
<img src="assets/images/big_scalability_plot.png" width="auto" height="500" alt="Big scalability plot">

#### **Bang**
<img src="assets/images/bang_scalability_plot.png" width="auto" height="500" alt="Bang scalability plot">

#### **Fork** (previously called Spawn)
<img src="assets/images/spawn_scalability_plot.png" width="auto" height="500" alt="Fork scalability plot">



## License
Distributed under the GNU General Public License v3.0. See [**LICENSE.md**](LICENSE.md) for more information.



## Contact
Daniel Larsen (iyyel) - [**iyyel.io**](https://iyyel.io) - [**me@iyyel.io**](mailto:me@iyyel.io)



## Acknowledgments
Alceste Scalas - [**alcsc**](https://people.compute.dtu.dk/alcsc/) - [**github**](https://github.com/alcestes)



<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/fio-fsharp/fio.svg?style=for-the-badge
[contributors-url]: https://github.com/fio-fsharp/fio/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/fio-fsharp/fio.svg?style=for-the-badge
[forks-url]: https://github.com/fio-fsharp/fio/network/members
[stars-shield]: https://img.shields.io/github/stars/fio-fsharp/fio.svg?style=for-the-badge
[stars-url]: https://github.com/fio-fsharp/fio/stargazers
[issues-shield]: https://img.shields.io/github/issues/fio-fsharp/fio.svg?style=for-the-badge
[issues-url]: https://github.com/fio-fsharp/fio/issues
[license-shield]: https://img.shields.io/github/license/fio-fsharp/fio.svg?style=for-the-badge
[license-url]: https://github.com/fio-fsharp/fio/blob/main/LICENSE.md
[nuget-shield]: https://img.shields.io/nuget/v/FIO.svg?style=for-the-badge
[nuget-url]: https://www.nuget.org/packages/FIO/0.0.9-alpha
