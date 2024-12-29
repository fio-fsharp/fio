[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]



<br />
<div align="center">
  <a href="https://github.com/iyyel/fio">
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
**FIO** is a type-safe, highly concurrent and asynchronous library for F# that is based on principles from pure functional programming. It provides a construct known as the IO monad for handling expressions with side effects. It uses the concept of "green threads" also known as "fibers" to provide scalable and efficient concurrency.

**FIO** is an attempt at creating a similar environment to that of [ZIO](https://zio.dev/) for Scala. **FIO** is both inspired by
[ZIO](https://zio.dev/) and [Cats Effect](https://typelevel.org/cats-effect/).

**FIO** was developed as part of a master's thesis in Computer Science and Engineering at the [Technical University of Denmark (DTU)](https://www.dtu.dk/english/). You can read the thesis, which provides more details about **FIO**, [here](https://iyyel.io/assets/doc/masters_thesis_daniel_larsen.pdf).

**DISCLAIMER:** **FIO** is in early development stages and a lot of improvements and enhancements can be made. This README might be lackluster.



## Built With
**FIO** is built using the following technologies:

* [F#](https://fsharp.org/)
* [.NET](https://dotnet.microsoft.com/en-us/)



## Getting Started
It is easy to get started with **FIO**.

* Download and install [.NET](https://dotnet.microsoft.com/en-us/)
* Download and install a compatible IDE such as [Visual Studio](https://visualstudio.microsoft.com/downloads/) or [Rider](https://www.jetbrains.com/rider/download/), or a text editor like [Visual Studio Code](https://code.visualstudio.com/)

* Download or clone this repository
* Open it in your IDE or text editor of choice
* Navigate to the _Examples_ project and check out the example programs or create a new file to start using **FIO**



## Usage
Create a new class and import the library using "open FSharp.FIO". For example:

```fsharp
open FSharp.FIO

[<EntryPoint>]
let main _ =
  let askForName =
    fio (fun () -> printfn "%s" "Hello! What is your name?")
    >> fun _ ->
    fio (fun () -> Console.ReadLine())
    >> fun name ->
    fio (fun () -> printfn $"Hello, %s{name}, welcome to FIO!")

  let fiber = Advanced.Runtime().Run askForName
  let result = fiber.Await()
  printfn $"%A{result}"
```



## Benchmarks
This repository contains five benchmarks that each tests an aspect of concurrent computing.
All benchmarks reside from the [Savina - An Actor Benchmark Suite](http://soft.vub.ac.be/AGERE14/papers/ageresplash2014_submission_19.pdf) paper.

* Pingpong (Message sending and retrieval)
* ThreadRing (Message sending and retrieval, context switching between fibers)
* Big (Contention on channel, many-to-many message passing)
* Bang (Many-to-one messaging)
* Spawn (Spawning time of fibers)

The benchmarks can be given the following command line options:

```
OPTIONS:

    --naive-runtime       specify naive runtime. (specify only one runtime)
    --intermediate-runtime <evalworkercount> <blockingworkercount> <evalstepcount>
                          specify eval worker count, blocking worker count and eval step count for intermediate
                          runtime. (specify only one runtime)
    --advanced-runtime <evalworkercount> <blockingworkercount> <evalstepcount>
                          specify eval worker count, blocking worker count and eval step count for advanced runtime.
                          (specify only one runtime)
    --deadlocking-runtime <evalworkercount> <blockingworkercount> <evalstepcount>
                          specify eval worker count, blocking worker count and eval step count for deadlocking
                          runtime. (specify only one runtime)
    --runs <runs>         specify the number of runs for each benchmark.
    --process-increment <processcountinc> <inctimes>
                          specify the value of process count increment and how many times.
    --pingpong <roundcount>
                          specify round count for pingpong benchmark.
    --threadring <processcount> <roundcount>
                          specify process count and round count for threadring benchmark.
    --big <processcount> <roundcount>
                          specify process count and round count for big benchmark.
    --bang <processcount> <roundcount>
                          specify process count and round count for bang benchmark.
    --spawn <processcount>
                          specify process count for spawn benchmark.
    --help                display this list of options.           display this list of options.
```

For example, running 30 runs of each benchmark using the advanced runtime with 7 evaluation workers, 1 blocking worker and 15 evaluation steps would look as so:

```
--advanced-runtime 7 1 15 --runs 30 --pingpong 120000 --threadring 2000 1 --big 500 1 --bang 3000 1 --spawn 3000
```

Additionally, the **FIO** project supports two conditional compilation options:

* **DETECT_DEADLOCK:** Enables a naive deadlock detecting thread that attempts to detect if a deadlock has occurred when running FIO programs
* **MONITOR:** Enables a monitoring thread that prints out data structure content during when running FIO programs

**DISCLAIMER:** These features are very experimental.



## Performance
Below the scalability of each interpreter can be seen for each benchmark. **I** is denoting the intermediate runtime and **A** the advanced. To give some insight into the interpreters, the naive interpreter uses operating system threads, the intermediate uses fibers with handling of blocked FIO programs in linear time, and the advanced uses fibers with constant time handling.

#### **Threadring**
<img src="assets/images/threadring_scalability_plot.png" width="auto" height="500" alt="Threadring scalability plot">
 
#### **Big**
<img src="assets/images/big_scalability_plot.png" width="auto" height="500" alt="Threadring scalability plot">

#### **Bang**
<img src="assets/images/bang_scalability_plot.png" width="auto" height="500" alt="Threadring scalability plot">

#### **Spawn**
<img src="assets/images/spawn_scalability_plot.png" width="auto" height="500" alt="Threadring scalability plot">



## License
Distributed under the GNU General Public License v3.0. See [LICENSE.md](LICENSE.md) for more information.



## Contact
Daniel Larsen (iyyel) - [iyyel.io](https://iyyel.io) - [hello@iyyel.io](mailto:hello@iyyel.io)



## Acknowledgments
Alceste Scalas - [alcsc](https://people.compute.dtu.dk/alcsc/) - [github](https://github.com/alcestes)



<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/iyyel/fio.svg?style=for-the-badge
[contributors-url]: https://github.com/iyyel/fio/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/iyyel/fio.svg?style=for-the-badge
[forks-url]: https://github.com/iyyel/fio/network/members
[stars-shield]: https://img.shields.io/github/stars/iyyel/fio.svg?style=for-the-badge
[stars-url]: https://github.com/iyyel/fio/stargazers
[issues-shield]: https://img.shields.io/github/issues/iyyel/fio.svg?style=for-the-badge
[issues-url]: https://github.com/iyyel/fio/issues
[license-shield]: https://img.shields.io/github/license/iyyel/fio.svg?style=for-the-badge
[license-url]: https://github.com/iyyel/fio/blob/main/LICENSE.md
