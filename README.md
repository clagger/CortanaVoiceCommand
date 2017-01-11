# CortanaVoiceCommand
Windows UWP app which implements custom voice commandos for interacting with Cortana

This application offers the opportunity to ask Cortana about the population of countries from the past (1960+) and predicts
the future population.

## How to use it

* pull or zip project
* compile it with Visual Studio 
* to start the application, start cortana and speak clear "Listen Up"

Then you can ask three different queries:

#### Query past population of countries

Example: Listen up, show me the population of Afghanistan in 1997
```sh
show me the population of [country] in [year]
```
#### Query future population of countries
Example: Listen up, how high will the population of Afghanistan be in 2020?
```sh
how high will the population of [country] be in [year]
```

or
```sh
how big will the population of [country] be in [year]
```
#### Query past women proportion of countries
Example: Listen up, how high was the proportion of women in Afghanistan in 2009?
```sh
how high was the proportion of women in [country]  in [year]
```

<b>this project is based on a Windows-universal-samples project</b>

https://github.com/Microsoft/Windows-universal-samples/tree/master/Samples/CortanaVoiceCommand

