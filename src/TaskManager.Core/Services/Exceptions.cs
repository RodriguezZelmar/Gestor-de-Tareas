namespace TaskManager.Core.Services;

/// <summary>Error de dominio con un mensaje apto para mostrar al usuario.</summary>
public class DomainException(string message) : Exception(message);

public sealed class ValidationException(string message) : DomainException(message);

/// <summary>El recurso no existe o no pertenece al usuario actual (no se distingue a propósito).</summary>
public sealed class NotFoundException(string what) : DomainException($"{what} no existe.");

public sealed class AuthException(string message) : DomainException(message);
