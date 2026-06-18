using Azure.Identity;
using McpProxy.Sdk.Exceptions;

namespace McpProxy.Tests.Unit.Exceptions;

public class BackendAuthClassifierTests
{
    public class IsAuthFailureTests
    {
        [Fact]
        public void Returns_True_For_Azure_AuthenticationFailedException()
        {
            BackendAuthClassifier.IsAuthFailure(new AuthenticationFailedException("expired")).Should().BeTrue();
        }

        [Fact]
        public void Returns_True_For_Azure_CredentialUnavailableException()
        {
            BackendAuthClassifier.IsAuthFailure(new CredentialUnavailableException("no browser")).Should().BeTrue();
        }

        [Fact]
        public void Returns_True_For_BackendAuthenticationException()
        {
            var ex = new BackendAuthenticationException("mail", "m365", "needs sign-in");
            BackendAuthClassifier.IsAuthFailure(ex).Should().BeTrue();
        }

        [Fact]
        public void Returns_True_For_Msal_Backed_AuthenticationException()
        {
            var ex = new McpProxy.Sdk.Authentication.AuthenticationException("token acquisition failed");
            BackendAuthClassifier.IsAuthFailure(ex).Should().BeTrue();
        }

        [Fact]
        public void Returns_True_When_Auth_Failure_Is_Inner_Exception()
        {
            var inner = new AuthenticationFailedException("expired");
            var outer = new InvalidOperationException("wrapped", inner);
            BackendAuthClassifier.IsAuthFailure(outer).Should().BeTrue();
        }

        [Fact]
        public void Returns_False_For_Generic_Exception()
        {
            BackendAuthClassifier.IsAuthFailure(new InvalidOperationException("boom")).Should().BeFalse();
        }

        [Fact]
        public void Returns_False_For_Transport_Exception()
        {
            BackendAuthClassifier.IsAuthFailure(new HttpRequestException("connection refused")).Should().BeFalse();
        }

        [Fact]
        public void Returns_False_For_Null()
        {
            BackendAuthClassifier.IsAuthFailure(null).Should().BeFalse();
        }
    }

    public class FromTests
    {
        [Fact]
        public void Builds_Message_With_Server_Group_And_Remediation()
        {
            var inner = new AuthenticationFailedException("refresh token expired");

            var ex = BackendAuthenticationException.From("mail", "m365", inner);

            ex.ServerName.Should().Be("mail");
            ex.CredentialGroup.Should().Be("m365");
            ex.InnerException.Should().BeSameAs(inner);
            ex.Message.Should().Contain("mail");
            ex.Message.Should().Contain("m365");
            ex.Message.Should().Contain("sign-in");
        }

        [Fact]
        public void Omits_Group_When_Not_Set()
        {
            var ex = BackendAuthenticationException.From("mail", null, new AuthenticationFailedException("x"));

            ex.CredentialGroup.Should().BeNull();
            ex.Message.Should().Contain("mail");
        }
    }

    public class AggregateTests
    {
        [Fact]
        public void Returns_Single_Failure_Unchanged()
        {
            var failure = new BackendAuthenticationException("mail", "m365", "needs sign-in");

            var result = BackendAuthenticationException.Aggregate([failure]);

            result.Should().BeSameAs(failure);
        }

        [Fact]
        public void Combines_Multiple_Failures_Into_One_Message()
        {
            var a = new BackendAuthenticationException("mail", "m365", "mail needs sign-in");
            var b = new BackendAuthenticationException("calendar", "m365", "calendar needs sign-in");

            var result = BackendAuthenticationException.Aggregate([a, b]);

            result.Message.Should().Contain("mail");
            result.Message.Should().Contain("calendar");
        }
    }
}
