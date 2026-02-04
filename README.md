# Software Engineer Interview - Debugging Exercise

## Background

You're working on **KubeDeploy**, a Kubernetes deployment management platform. One of our core services is the **Deployment Status Aggregator**, which polls multiple clusters to get the current status of deployments and returns a consolidated view.

Recently, we've been getting reports from customers that deployment statuses are sometimes incorrect — some deployments show as "failed" when they should have succeeded. The issue is hard to reproduce consistently.

## Your Task

1. Review the code in this repository
2. Run the tests and observe the failing test
3. Identify the root cause of the bug
4. Explain the bug and propose a fix

**Time limit: 25-30 minutes**

## The Code

```
DeploymentStatusService/
├── Models.cs                              # Data models
├── RetryPolicy.cs                         # Retry logic with exponential backoff
├── DeploymentStatusAggregator.cs          # Main service
└── DeploymentStatusAggregatorTests.cs     # Tests (1 failing)
```

## Setup

### Prerequisites
- .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0))

### Run Tests
```bash
cd DeploymentStatusService
dotnet restore
dotnet test
```

You should see **4 passing tests and 1 failing test**. The failing test is what you need to investigate.

### Run Just the Failing Test
```bash
dotnet test --filter "HighVolume"
```

## What We're Looking For

- Your debugging approach and methodology
- Ability to trace through code flow
- Clear explanation of the root cause
- A reasonable fix proposal (you don't need to implement it)

## Notes

- You don't need deep Kubernetes knowledge — treat the cluster API as a simple HTTP service
- Feel free to add logging or modify the test to help debug
- Think out loud — we want to understand your thought process

Good luck!
