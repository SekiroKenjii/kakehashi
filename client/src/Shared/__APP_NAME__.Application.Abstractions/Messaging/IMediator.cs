namespace __ROOT_NAMESPACE__.Application.Abstractions.Messaging;

/// <summary>The single entry point for sending requests and publishing notifications.</summary>
public interface IMediator : ISender, IPublisher;
