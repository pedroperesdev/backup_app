# Program for Folder Synchronization built with **.NET** and written in **C#**.

## Description
Program that executes a one-way synchronization from source(original) folder to a replica folder. Once running, enter the following:  
sourceDir replicaDir intervalSeconds logFilePath  
separated by spaces.
Since the program synchronizes two folders, both sourceDir and replicaDir need to exist. The synchronization detects file/folder additions, removals and updates. Type QUIT to exit program.
Program is written in C#, located in App/Program.cs

## Installation
```sh
git clone https://github.com/pedroperesdev/backup_app.git
cd backup_app/App
dotnet run Program.cs
